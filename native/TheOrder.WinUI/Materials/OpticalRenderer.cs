using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace TheOrder.WinUI.Materials;

// UI-thread owner. One hardware device, one reusable source, two swapchain buffers.
// No desktop/window capture, background thread, periodic timer or idle rendering.
internal sealed class OpticalRenderer : IDisposable
{
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalCreate(IntPtr panel, out IntPtr result);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalUpload(IntPtr handle, byte[] bytes, uint w, uint h, uint length);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalDraw(IntPtr handle, uint w, uint h, float[] parameters);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalDrawBackground(IntPtr handle, uint w, uint h, float[] parameters);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalDrawEx(IntPtr handle, uint w, uint h, float[] parameters, uint count, int background);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalDrawForCache(IntPtr handle, uint w, uint h, float[] parameters, uint count);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalReadCachedFrame(IntPtr handle, [Out] byte[]? pixels, uint length);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalSceneFrost(IntPtr handle, float sigma);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalFrostDiagnostics(IntPtr handle, [Out] ulong[] values);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalSceneBegin(IntPtr handle, uint w, uint h, float scale, uint color);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalSceneRect(IntPtr handle, float x, float y, float w, float h, uint color);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int OpticalSceneText(IntPtr handle, string text, float x, float y, float size, uint color, uint weight);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int OpticalSceneImage(IntPtr handle, string path, float x, float y, float w, float h, int fill);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int OpticalSceneImageBlur(IntPtr handle, string path, float x, float y, float w, float h, int fill, float sigma);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalScenePixels(IntPtr handle, byte[] data, uint w, uint h, uint length, float x, float y, float width, float height, int fill, float sigma);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalSceneEnd(IntPtr handle);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalSceneDiagnostics(IntPtr handle, [Out] ulong[] values);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalDestroy(IntPtr handle);
    [DllImport("TheOrder.OpticalMaterial.dll", ExactSpelling = true)]
    private static extern int OpticalDiagnostics([Out] ulong[] counts, [Out] ulong[] rows, uint capacity, out uint count);
    internal static object Diagnostics()
    {
        var counts = new ulong[6]; var rows = new ulong[160];
        var hr = OpticalDiagnostics(counts, rows, (uint)rows.Length, out var count);
        return new { HResult = hr, RenderersCreated = counts[0], RenderersDestroyed = counts[1],
            Uploads = counts[2], SourceTexturesCreated = counts[3], SwapchainsCreated = counts[4], BufferResizes = counts[5],
            Adapters = Enumerable.Range(0, checked((int)count)).Select(i => new {
                LuidLow = rows[i*10], LuidHigh = rows[i*10+1], Flags = rows[i*10+2], Index = rows[i*10+3],
                LocalHResult = rows[i*10+4], LocalUsage = rows[i*10+5], LocalBudget = rows[i*10+6],
                NonLocalHResult = rows[i*10+7], NonLocalUsage = rows[i*10+8], NonLocalBudget = rows[i*10+9] }).ToArray() };
    }
    private IntPtr handle;
    private readonly int thread = Environment.CurrentManagedThreadId;
    internal long SceneUpdates { get; private set; }
    internal double SceneCpuMilliseconds { get; private set; }
    internal object SceneEvidence
    {
        get
        {
            var v = new ulong[7];
            var hr = Active ? OpticalSceneDiagnostics(handle, v) : 0;
            return new { HResult = hr, Completed = v[0], ImagesDrawn = v[1], TextsDrawn = v[2], TargetsCreated = v[3],
                Drawing = v[4], TemporaryImages = v[5], DeviceContexts = v[6], SceneCpuMilliseconds };
        }
    }
    internal long Draws { get; private set; }
    internal long Uploads { get; private set; }
    internal long SourceBytes { get; private set; }
    internal long BufferBytes { get; private set; }
    internal double DrawCpuMilliseconds { get; private set; }
    internal double MaxDrawCpuMilliseconds { get; private set; }
    internal bool Active => handle != IntPtr.Zero;
    internal bool Ready => SourceBytes != 0;
    internal OpticalRenderer(SwapChainPanel panel)
    {
        using var abi = MarshalInspectable<SwapChainPanel>.CreateMarshaler(panel);
        Marshal.ThrowExceptionForHR(OpticalCreate(abi.ThisPtr, out handle));
    }
    internal void Upload(byte[] pixels, int width, int height)
    {
        ObjectDisposedException.ThrowIf(!Active, this);
        Marshal.ThrowExceptionForHR(OpticalUpload(handle, pixels, checked((uint)width), checked((uint)height), checked((uint)pixels.Length)));
        SourceBytes = pixels.LongLength; Uploads++;
    }
    internal void Draw(int width, int height, float[] parameters, bool backgroundOnly = false, bool cacheFrame = false)
    {
        ObjectDisposedException.ThrowIf(!Active, this);
        // 20 constants = accepted C1 layout; 24 adds rev1 frost (c[17], tint c[20..23]); 28 adds rev2 continuous glass (c[24..27]).
        if (parameters.Length is not (20 or 24 or 28) || parameters.Any(x => !float.IsFinite(x))) throw new ArgumentException("Finite optical constants required");
        var begin = Stopwatch.GetTimestamp();
        Marshal.ThrowExceptionForHR(cacheFrame
            ? OpticalDrawForCache(handle, checked((uint)width), checked((uint)height), parameters, (uint)parameters.Length)
            : parameters.Length != 20
            ? OpticalDrawEx(handle, checked((uint)width), checked((uint)height), parameters, (uint)parameters.Length, backgroundOnly ? 1 : 0)
            : backgroundOnly
                ? OpticalDrawBackground(handle, checked((uint)width), checked((uint)height), parameters)
                : OpticalDraw(handle, checked((uint)width), checked((uint)height), parameters));
        var milliseconds = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        DrawCpuMilliseconds += milliseconds; MaxDrawCpuMilliseconds = Math.Max(MaxDrawCpuMilliseconds, milliseconds);
        Draws++; BufferBytes = (long)width * height * 8;
    }
    // 0 = copied, 1 = GPU still busy, negative = frame unavailable.
    internal int ReadCachedFrame(byte[] pixels) => OpticalReadCachedFrame(handle, pixels, checked((uint)pixels.Length));
    internal void DiscardCachedFrame() => _ = OpticalReadCachedFrame(handle, null, 0);
    // Artwork stage: one revision per cover/size/theme change. The ambient band is the app's own decoded
    // cover pixels (never a capture), filled and blurred on the renderer's device, then a polarity scrim.
    internal void RenderArtwork(TheOrder.Application.RasterPixels art, double widthDip, double heightDip, double scale, float sigma, uint background, uint scrim, bool contain = false)
    {
        ObjectDisposedException.ThrowIf(!Active, this);
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Owning UI thread required");
        if (!double.IsFinite(scale) || scale <= 0 || scale > 6 || !double.IsFinite(widthDip) || !double.IsFinite(heightDip) || widthDip < 1 || heightDip < 1 || widthDip * scale > 8192 || heightDip * scale > 8192)
            throw new ArgumentOutOfRangeException(nameof(scale));
        var width = checked((uint)Math.Ceiling(widthDip * scale));
        var height = checked((uint)Math.Ceiling(heightDip * scale));
        var begin = Stopwatch.GetTimestamp();
        Marshal.ThrowExceptionForHR(OpticalSceneBegin(handle, width, height, (float)scale, background));
        var ended = false;
        try
        {
            var ratio = contain ? Math.Min(widthDip / art.Width, heightDip / art.Height) : 1;
            var destinationWidth = contain ? art.Width * ratio : widthDip;
            var destinationHeight = contain ? art.Height * ratio : heightDip;
            Marshal.ThrowExceptionForHR(OpticalScenePixels(handle, art.Bgra, checked((uint)art.Width), checked((uint)art.Height), checked((uint)art.Bgra.Length),
                (float)(contain ? widthDip - destinationWidth : 0), (float)((heightDip - destinationHeight) / 2), (float)destinationWidth, (float)destinationHeight, contain ? 0 : 1, sigma));
            if ((scrim >> 24) != 0) Marshal.ThrowExceptionForHR(OpticalSceneRect(handle, 0, 0, (float)widthDip, (float)heightDip, scrim));
            var result = OpticalSceneEnd(handle); ended = true;
            Marshal.ThrowExceptionForHR(result);
            SourceBytes = (long)width * height * 4; SceneUpdates++;
        }
        finally
        {
            if (!ended) _ = OpticalSceneEnd(handle);
            SceneCpuMilliseconds += Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        }
    }
    internal void RenderScene(GalleryScene scene, double scale)
    {
        ObjectDisposedException.ThrowIf(!Active, this);
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Owning UI thread required");
        if (!double.IsFinite(scale) || scale <= 0 || scale > 6 || scene.Primitives.Count > 300)
            throw new ArgumentOutOfRangeException(nameof(scale));
        var width = checked((uint)Math.Ceiling(GalleryScene.Width * scale));
        var height = checked((uint)Math.Ceiling(GalleryScene.Height * scale));
        var begin = Stopwatch.GetTimestamp();
        Marshal.ThrowExceptionForHR(OpticalSceneBegin(handle, width, height, (float)scale, scene.Background));
        var ended = false;
        try
        {
            foreach (var item in scene.Primitives)
            {
                var hr = item.Kind switch
                {
                    0 => OpticalSceneRect(handle, item.X, item.Y, item.Width, item.Height, item.Color),
                    1 => OpticalSceneText(handle, item.Content!, item.X, item.Y, item.Width, item.Color, checked((uint)item.Option)),
                    2 => OpticalSceneImage(handle, item.Content!, item.X, item.Y, item.Width, item.Height, item.Option),
                    3 => OpticalSceneImageBlur(handle, item.Content!, item.X, item.Y, item.Width, item.Height, item.Option, item.Color),
                    _ => throw new InvalidDataException("Unknown Gallery primitive")
                };
                Marshal.ThrowExceptionForHR(hr);
            }
            var result = OpticalSceneEnd(handle); ended = true;
            Marshal.ThrowExceptionForHR(result);
            SourceBytes = (long)width * height * 4; SceneUpdates++;
        }
        finally
        {
            if (!ended) _ = OpticalSceneEnd(handle); // pair Begin even when decoding/drawing failed; owner then fails closed.
            SceneCpuMilliseconds += Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        }
    }
    internal long FrostBytes { get; private set; }
    internal double FrostCpuMilliseconds { get; private set; }
    internal double MaxFrostCpuMilliseconds { get; private set; }
    // Once per completed scene revision. sigma <= 0 retires the blurred copy.
    internal void Frost(double sigmaDip)
    {
        ObjectDisposedException.ThrowIf(!Active, this);
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Owning UI thread required");
        var begin = Stopwatch.GetTimestamp();
        Marshal.ThrowExceptionForHR(OpticalSceneFrost(handle, (float)sigmaDip));
        var milliseconds = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        FrostCpuMilliseconds += milliseconds; MaxFrostCpuMilliseconds = Math.Max(MaxFrostCpuMilliseconds, milliseconds);
        FrostBytes = sigmaDip > 0 ? SourceBytes : 0;
    }
    internal object FrostEvidence
    {
        get
        {
            var v = new ulong[4];
            var hr = Active ? OpticalFrostDiagnostics(handle, v) : 0;
            return new { HResult = hr, TexturesCreated = v[0], Passes = v[1], Valid = v[2] == 1, TextureAlive = v[3] == 1,
                FrostBytes, FrostCpuMilliseconds, MaxFrostCpuMilliseconds };
        }
    }
    public void Dispose()
    {
        if (!Active) return;
        // Failed detach leaves ownership intact so Gallery can retry cleanup.
        Marshal.ThrowExceptionForHR(OpticalDestroy(handle));
        handle = IntPtr.Zero; SourceBytes = BufferBytes = FrostBytes = 0;
    }
}
