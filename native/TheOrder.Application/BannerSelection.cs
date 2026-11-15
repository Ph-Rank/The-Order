using TheOrder.Domain;

namespace TheOrder.Application;

public sealed record BannerChoice(string Key, string Url, double Score, string Kind, string SourceWorkId = "", string? ReferenceTitle = null, bool TextScreened = false)
{
    public int Width { get; init; }
    public int Height { get; init; }
    public BannerComposition? Composition { get; init; }
}
public sealed record BannerAssessment(string Url, string Kind, int Width, int Height, string Result, BannerComposition? Composition = null);
public readonly record struct BannerCrop(int X, int Y, int Width, int Height, bool FitEntire);

// Bounded image analysis. Explicit version references may supply missing CGs; poster
// preferences and library files are never changed.
public static class BannerSelection
{
    public const int MaximumPreviews = 32;
    public const int TextAnalysisEdge = 768;
    public const double PreferredAspect = 16d / 9;
    public static IEnumerable<CatalogArt> Candidates(WorkMetadata work)
    {
        // Gallery covers may be discs, backs or book jackets, even when landscape.
        // Keep them out of automatic CG selection; Eligible still validates legacy caches.
        var pool = Eligible(work).Where(a => a.Kind != "cover").ToArray();
        if (pool.Length <= MaximumPreviews) return pool.OrderByDescending(Quality);
        var sharp = pool.OrderByDescending(a => a.Kind is "vndb-curated" or "steam-hero").ThenByDescending(Quality).Take(MaximumPreviews / 2).ToArray();
        var remaining = pool.Where(a => !sharp.Any(s => s.Url == a.Url)).ToArray();
        return sharp.Concat(Enumerable.Range(0, MaximumPreviews / 2).Select(i => remaining[(int)Math.Round(i * (remaining.Length - 1d) / (MaximumPreviews / 2 - 1))]));
    }
    public static IEnumerable<CatalogArt> Eligible(WorkMetadata work)
    {
        var arts = work.Presentation?.Art.AsEnumerable() ?? Enumerable.Empty<CatalogArt>();
        if (work.BannerUrl is { Length: > 0 } banner && !arts.Any(a => a.Url == banner)) arts = arts.Prepend(new(banner, 1280, 720, "banner"));
        if (work.Covers is { } covers) arts = arts.Concat(covers.Where(c => c.Width >= 640 && c.Height > 0).Select(c => new CatalogArt(c.Url, c.Width!.Value, c.Height!.Value, "cover")));
        return arts.Where(a => a.Width >= 640 && a.Height >= 180 && a.Width / (double)a.Height is >= 1.35 and <= 4.5)
            .DistinctBy(a => a.Url).Take(64).ToArray();
    }
    private static double Quality(CatalogArt art) => Math.Min(art.Width, art.Height * 3.3);
    public sealed record Outcome(BannerChoice? Choice, bool Complete)
    {
        public BannerAssessment[] Assessments { get; init; } = [];
    }
    public static async Task<BannerChoice?> SelectAsync(WorkMetadata work, IImagePipeline images, IRemoteCoverRegistry registry, CancellationToken cancellation, IArtworkTextDetector? textDetector = null)
        => (await AnalyzeAsync(work, null, images, registry, cancellation, textDetector).ConfigureAwait(false)).Choice;
    public static async Task<BannerChoice?> SelectAsync(WorkMetadata work, WorkMetadata? reference, IImagePipeline images, IRemoteCoverRegistry registry, CancellationToken cancellation, IArtworkTextDetector? textDetector = null)
        => (await AnalyzeAsync(work, reference, images, registry, cancellation, textDetector).ConfigureAwait(false)).Choice;
    public static async Task<Outcome> AnalyzeAsync(WorkMetadata work, WorkMetadata? reference, IImagePipeline images, IRemoteCoverRegistry registry, CancellationToken cancellation, IArtworkTextDetector? textDetector = null)
    {
        var own = await AnalyzeOneAsync(work, images, registry, cancellation, textDetector).ConfigureAwait(false);
        if (reference is null) return own;
        return await ContinueWithReferenceAsync(work, own, reference, images, registry, cancellation, textDetector).ConfigureAwait(false);
    }
    internal static async Task<Outcome> ContinueWithReferenceAsync(WorkMetadata work, Outcome own, WorkMetadata reference, IImagePipeline images, IRemoteCoverRegistry registry, CancellationToken cancellation, IArtworkTextDetector? textDetector)
    {
        if (own.Choice is not null || CatalogReferences.Preferred(work)?.Id != reference.Id) return own;
        var related = await AnalyzeOneAsync(reference, images, registry, cancellation, textDetector).ConfigureAwait(false);
        return new(related.Choice is null ? null : related.Choice with { ReferenceTitle = reference.Title },
            own.Complete && related.Complete) { Assessments = own.Assessments.Concat(related.Assessments).ToArray() };
    }
    private static async Task<Outcome> AnalyzeOneAsync(WorkMetadata work, IImagePipeline images, IRemoteCoverRegistry registry, CancellationToken cancellation, IArtworkTextDetector? textDetector)
    {
        BannerChoice? best = null; RasterPixels? bestPreview = null; bool complete = true;
        var assessments = new List<BannerAssessment>();
        foreach (var art in Candidates(work))
        {
            cancellation.ThrowIfCancellationRequested(); var key = registry.Register(art.Url, work.FetchedAt);
            if (key is null) { complete = false; continue; }
            try
            {
                var result = await images.LoadAsync(key, textDetector is null ? 256 : TextAnalysisEdge, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (result.Pixels is not { } pixels) { complete = false; assessments.Add(new(art.Url, art.Kind, art.Width, art.Height, "unavailable")); continue; }
                if (pixels.Width / (double)pixels.Height < 1.35) { assessments.Add(new(art.Url, art.Kind, art.Width, art.Height, "not-landscape")); continue; }
                if (textDetector is not null)
                {
                    var text = await textDetector.AnalyzeAsync(pixels, cancellation).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    if (!text.Available) { complete = false; assessments.Add(new(art.Url, art.Kind, art.Width, art.Height, "detector-unavailable")); continue; }
                    if (ArtworkTextRules.ContainsOverlayText(text, allowTitleMark: art.Kind == ArtworkSources.VndbPromoKind)) { assessments.Add(new(art.Url, art.Kind, art.Width, art.Height, "overlay-text")); continue; }
                }
                if (!(textDetector is null ? Score(pixels, art.Width, art.Height) >= -.1 : HasSceneVariation(pixels)))
                { assessments.Add(new(art.Url, art.Kind, art.Width, art.Height, "blank-or-interface")); continue; }
                // Metadata can claim a larger size than the actual decoded original.
                // A raster smaller than the requested edge has not been downsampled.
                int edge = textDetector is null ? 256 : TextAnalysisEdge;
                int width = Math.Max(pixels.Width, pixels.Height) < edge ? pixels.Width : art.Width;
                int height = Math.Max(pixels.Width, pixels.Height) < edge ? pixels.Height : art.Height;
                var composition = BannerAesthetics.Analyze(pixels, width, height);
                var preview = ScoringPreview(pixels);
                var score = composition.Score;
                assessments.Add(new(art.Url, art.Kind, width, height, "eligible", composition));
                bool same = bestPreview is not null && BannerAesthetics.SameScene(preview, bestPreview);
                bool upgrade = same && width > best!.Width * 1.15 && height > best.Height * 1.15;
                bool downgrade = same && (width < best!.Width / 1.15 || height < best.Height / 1.15);
                // Comparable distinct scenes keep the catalog's stable order. Near-duplicates
                // use their original resolution, not a tiny compression-induced score change.
                if (best is null || upgrade || !downgrade && score > best.Score + 1.5)
                { best = new(key, art.Url, score, art.Kind, work.Id, TextScreened: textDetector is not null)
                    { Width = width, Height = height, Composition = composition }; bestPreview = preview; }
            }
            catch (OperationCanceledException) { throw; }
            catch { complete = false; }
        }
        return new(best, complete) { Assessments = assessments.ToArray() };
    }
    private static bool HasSceneVariation(RasterPixels image)
    {
        Validate(image); double sum = 0, square = 0; int n = 0, step = Math.Max(1, Math.Max(image.Width, image.Height) / 80);
        for (int y = 0; y < image.Height; y += step) for (int x = 0; x < image.Width; x += step)
        { var l = Lum(image, x, y); sum += l; square += l * l; n++; }
        return n > 0 && square / n - Math.Pow(sum / n, 2) > .0008;
    }
    public static double Score(RasterPixels image, int sourceWidth, int sourceHeight, double preferredAspect = PreferredAspect)
    {
        Validate(image); image = ScoringPreview(image); double saturation = 0, dark = 0, light = 0, edges = 0, orthogonal = 0; int count = 0;
        var step = Math.Max(1, Math.Max(image.Width, image.Height) / 96);
        for (var y = step; y < image.Height - step; y += step) for (var x = step; x < image.Width - step; x += step)
        {
            var (r, g, b) = Pixel(image, x, y); var hi = Math.Max(r, Math.Max(g, b)); var lo = Math.Min(r, Math.Min(g, b));
            saturation += hi == 0 ? 0 : (hi - lo) / hi; var l = Lum(image, x, y); if (l < .08) dark++; if (l > .96) light++;
            var dx = Math.Abs(l - Lum(image, x + step, y)); var dy = Math.Abs(l - Lum(image, x, y + step));
            edges += Math.Min(1, (dx + dy) * 3); if (Math.Max(dx, dy) > .15 && Math.Min(dx, dy) < .025 && hi - lo < .14) orthogonal++;
            count++;
        }
        if (count == 0 || (dark + light) / count > .97) return -100;
        // OCR excludes dialogue/menu images first. Usable source resolution then carries enough
        // weight to stop a 640px preview beating the clean 1280/1920px scene beside it.
        var ratio = image.Width / (double)image.Height;
        return .45 - .65 * Math.Abs(Math.Log(ratio / preferredAspect)) + .65 * Math.Min(Math.Min(sourceWidth, sourceHeight * preferredAspect) / 1920d, 1)
            + .12 * saturation / count - .10 * edges / count - .35 * (dark + light) / count - 4 * orthogonal / count - 1.2 * TextLayout(image);
    }
    private static RasterPixels ScoringPreview(RasterPixels image)
    {
        if (Math.Max(image.Width, image.Height) <= 256) return image;
        var scale = 256d / Math.Max(image.Width, image.Height);
        int width = Math.Max(1, (int)Math.Round(image.Width * scale)), height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var bytes = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            Buffer.BlockCopy(image.Bgra, ((y * image.Height / height) * image.Width + x * image.Width / width) * 4, bytes, (y * width + x) * 4, 4);
        return new(width, height, bytes);
    }
    private static double TextLayout(RasterPixels image)
    {
        // Small bright connected strokes arranged on baselines. This is a soft clutter measure,
        // not OCR or an assertion about the image's contents. Scale the bounded 256px preview.
        var w = image.Width; var h = image.Height; var mask = new byte[w * h];
        for (var y = 2; y < h - 2; y++) for (var x = 2; x < w - 2; x++)
        {
            var (r, g, b) = Pixel(image, x, y); var spread = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            if (Lum(image, x, y) > .77 && spread < .17 && Math.Min(Math.Min(Lum(image, x - 2, y), Lum(image, x + 2, y)), Math.Min(Lum(image, x, y - 2), Lum(image, x, y + 2))) < .45) mask[y * w + x] = 1;
        }
        var strokes = new List<(int X, int Y)>(); var stack = new Stack<int>();
        for (var p = 0; p < mask.Length; p++) if (mask[p] != 0)
        {
            mask[p] = 0; stack.Push(p); var pixels = 0; var xmin = w; var xmax = 0; var ymin = h; var ymax = 0;
            while (stack.TryPop(out var at))
            {
                var x = at % w; var y = at / w; pixels++; xmin = Math.Min(xmin, x); xmax = Math.Max(xmax, x); ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y);
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx; var ny = y + dy; if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    var next = ny * w + nx; if (mask[next] == 0) continue; mask[next] = 0; stack.Push(next);
                }
            }
            if (pixels is >= 2 and <= 40 && xmax - xmin <= 15 && ymax - ymin <= 8) { strokes.Add((xmin, ymin)); if (strokes.Count >= 512) return 1; }
        }
        var aligned = strokes.Count(a => strokes.Count(b => Math.Abs(a.Y - b.Y) < 3 && Math.Abs(a.X - b.X) is > 5 and < 100) >= 3);
        return Math.Min(1, aligned / 60d);
    }
    public static BannerCrop Plan(RasterPixels image, double targetAspect)
    {
        Validate(image); if (!double.IsFinite(targetAspect) || targetAspect <= 0) throw new ArgumentOutOfRangeException(nameof(targetAspect));
        var aspect = image.Width / (double)image.Height; var retained = Math.Min(aspect / targetAspect, targetAspect / aspect);
        // Heavy crops magnify faces and remove heads/scene context. Keep the entire landscape instead.
        if (retained < .62) return new(0, 0, image.Width, image.Height, true);
        int width = Math.Min(image.Width, Math.Max(1, (int)Math.Round(image.Height * targetAspect)));
        int height = Math.Min(image.Height, Math.Max(1, (int)Math.Round(image.Width / targetAspect)));
        double best = double.NegativeInfinity; int bx = (image.Width - width) / 2, by = (image.Height - height) / 2;
        for (int i = 0; i <= 10; i++)
        {
            int x = (image.Width - width) * i / 10, y = (image.Height - height) * i / 10; double score = 0; int count = 0;
            var step = Math.Max(1, Math.Max(image.Width, image.Height) / 80);
            for (int sy = y; sy < y + height; sy += step) for (int sx = x; sx < x + width; sx += step)
            {
                var (r, g, b) = Pixel(image, sx, sy); var sat = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                // Warm foreground and upper scene detail are soft hints, never game/character-specific IDs.
                var warm = r > .45 && r > b * 1.12 && g > b * .95 && r - g < .28 ? .6 : 0;
                var topWeight = 1 - .35 * sy / image.Height; score += (sat + warm + .15) * topWeight; count++;
            }
            score = score / Math.Max(count, 1) - .08 * Math.Abs(i / 10d - .4);
            if (score > best) { best = score; bx = x; by = y; }
        }
        return new(bx, by, width, height, false);
    }
    public static RasterPixels Crop(RasterPixels image, BannerCrop crop)
    {
        Validate(image); if (crop.Width < 1 || crop.Height < 1 || crop.X < 0 || crop.Y < 0 || crop.Width > image.Width || crop.Height > image.Height || crop.X > image.Width - crop.Width || crop.Y > image.Height - crop.Height) throw new ArgumentOutOfRangeException(nameof(crop));
        if (crop.FitEntire || crop.Width == image.Width && crop.Height == image.Height) return image;
        var buffer = new byte[checked(crop.Width * crop.Height * 4)];
        for (int y = 0; y < crop.Height; y++) Buffer.BlockCopy(image.Bgra, ((y + crop.Y) * image.Width + crop.X) * 4, buffer, y * crop.Width * 4, crop.Width * 4);
        return new(crop.Width, crop.Height, buffer);
    }
    public static RasterPixels ComposeBackdrop(RasterPixels image, BannerCrop crop, double targetAspect)
    {
        var cropped = Crop(image, crop); if (!crop.FitEntire) return cropped;
        Validate(image); if (!double.IsFinite(targetAspect) || targetAspect <= 0) throw new ArgumentOutOfRangeException(nameof(targetAspect));
        var edge = Math.Min(ArtworkSizing.MaximumEdge, Math.Max(image.Width, image.Height));
        var w = targetAspect >= 1 ? edge : Math.Max(1, (int)Math.Round(edge * targetAspect));
        var h = targetAspect >= 1 ? Math.Max(1, (int)Math.Round(edge / targetAspect)) : edge;
        // A low-detail continuation fills the spare area; the complete landscape remains at the
        // right, at its original aspect ratio. This avoids both enlarged poster-like crops and a black seam.
        const int gw = 16, gh = 8; var grid = new double[gw * gh * 3];
        for (int gy = 0; gy < gh; gy++) for (int gx = 0; gx < gw; gx++)
        {
            int x0 = gx * image.Width / gw, x1 = Math.Max(x0 + 1, (gx + 1) * image.Width / gw);
            int y0 = gy * image.Height / gh, y1 = Math.Max(y0 + 1, (gy + 1) * image.Height / gh); int count = 0;
            for (var y = y0; y < Math.Min(image.Height, y1); y++) for (var x = x0; x < Math.Min(image.Width, x1); x++)
            { var at = (y * image.Width + x) * 4; for (var c = 0; c < 3; c++) grid[(gy * gw + gx) * 3 + c] += image.Bgra[at + c]; count++; }
            for (var c = 0; c < 3; c++) grid[(gy * gw + gx) * 3 + c] /= Math.Max(1, count);
        }
        var bytes = new byte[checked(w * h * 4)];
        var scale = Math.Min(w / (double)image.Width, h / (double)image.Height);
        int iw = Math.Max(1, (int)Math.Round(image.Width * scale)), ih = Math.Max(1, (int)Math.Round(image.Height * scale));
        int ix = w - iw, iy = (h - ih) / 2; var feather = Math.Max(1, iw / 18);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            double tx = x / (double)Math.Max(1, w - 1) * (gw - 1), ty = y / (double)Math.Max(1, h - 1) * (gh - 1);
            int gx = Math.Min(gw - 2, (int)tx), gy = Math.Min(gh - 2, (int)ty); double fx = tx - gx, fy = ty - gy;
            var at = (y * w + x) * 4;
            bool inside = x >= ix && y >= iy && x < ix + iw && y < iy + ih;
            int source = inside ? (Math.Min(image.Height - 1, (int)((y - iy) / scale)) * image.Width + Math.Min(image.Width - 1, (int)((x - ix) / scale))) * 4 : 0;
            double mix = inside ? Math.Min(1, (x - ix) / (double)feather) : 0;
            for (int c = 0; c < 3; c++)
            {
                double top = grid[(gy * gw + gx) * 3 + c] * (1 - fx) + grid[(gy * gw + gx + 1) * 3 + c] * fx;
                double bottom = grid[((gy + 1) * gw + gx) * 3 + c] * (1 - fx) + grid[((gy + 1) * gw + gx + 1) * 3 + c] * fx;
                bytes[at + c] = (byte)Math.Clamp((top * (1 - fy) + bottom * fy) * (1 - mix) + (inside ? image.Bgra[source + c] : 0) * mix, 0, 255);
            }
            bytes[at + 3] = 255;
        }
        return new(w, h, bytes);
    }
    private static void Validate(RasterPixels image) { if (image.Width < 1 || image.Height < 1 || image.Bgra.Length != checked(image.Width * image.Height * 4)) throw new ArgumentException("Invalid raster"); }
    private static (double R, double G, double B) Pixel(RasterPixels p, int x, int y) { var at = (y * p.Width + x) * 4; return (p.Bgra[at + 2] / 255d, p.Bgra[at + 1] / 255d, p.Bgra[at] / 255d); }
    private static double Lum(RasterPixels p, int x, int y) { var (r, g, b) = Pixel(p, x, y); return .2126 * r + .7152 * g + .0722 * b; }
}
