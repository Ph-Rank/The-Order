using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Infrastructure.Online;

// API shape is confined here, never exposed as a Domain object or to a View.
public sealed partial record RemoteWorkDto(JsonElement Value)
{
    public static JsonElement Field(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : default;
    public static string Text(JsonElement value, string? name = null) { var field = name is null ? value : Field(value, name); return field.ValueKind == JsonValueKind.String ? field.GetString()!.Trim() : ""; }
    public static IEnumerable<JsonElement> Rows(JsonElement value, string name) { var field = Field(value, name); return field.ValueKind == JsonValueKind.Array ? field.EnumerateArray() : []; }
    public static bool ValidId(string value) => IdPattern().IsMatch(value);
    internal static string? KunGalGalgameId(JsonElement work)
    {
        var claim = Field(work, "claim"); var id = Text(claim, "site_work_id");
        return Text(work, "object") == "work" && Text(work, "medium") == "galgame" &&
            Text(claim, "site") == "kungal" && Text(claim, "state") is "live" or "draft" && ValidId(id) ? id : null;
    }
    internal static string Localized(JsonElement value)
    {
        var names = Field(value, "localized");
        return new[] { Text(Field(names, "zh-Hans"), "value"), Text(Field(names, "zh-Hant"), "value"), Text(value, "display_name") }.FirstOrDefault(s => s.Length > 0) ?? "";
    }
    private static ImmutableArray<string> Unique(IEnumerable<string> source) => [.. source.Where(s => s.Length > 0).Distinct(StringComparer.Ordinal)];
    internal static string? Image(JsonElement value)
    {
        var raw = Text(value, "url");
        return Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 ? uri.AbsoluteUri : null;
    }
    private static double? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
    private static long? Integer(JsonElement value, long maximum = 9007199254740991) { var n = Number(value); return n is >= 0 && n <= maximum && Math.Floor(n.Value) == n ? (long)n.Value : null; }
    private static int? Dimension(JsonElement value) { var n = Integer(value, 65535); return n > 0 ? (int)n : null; }
    internal static string? ReferenceUrl(string source, string id)
    {
        if (source == "vndb" && Regex.IsMatch(id, "^[vr][0-9]+$")) return "https://vndb.org/" + id;
        if (source == "dlsite" && Regex.IsMatch(id, "^(?:RJ|VJ|BJ)[0-9]{6,12}$")) return "https://www.dlsite.com/home/work/=/product_id/" + id + ".html";
        if (!ValidId(id)) return null;
        return source switch { "bangumi" => "https://bgm.tv/subject/" + id, "steam" => "https://store.steampowered.com/app/" + id + "/",
            "erogamescape" => "https://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/game.php?game=" + id, "howlongtobeat" => "https://howlongtobeat.com/game/" + id, _ => null };
    }
    private static readonly HashSet<string> ReferenceHosts = ["vndb.org", "bgm.tv", "bangumi.tv", "chii.in", "store.steampowered.com", "erogamescape.dyndns.org", "howlongtobeat.com", "www.dlsite.com", "www.kungal.com"];
    public WorkMetadata Map(bool includeExplicit, DateTimeOffset now)
    {
        var work = Value; var id = Text(work, "id");
        // WorkMetadata is the local Galgame model, not a generic ACGN work.
        // Older catalog snapshots omitted medium; preserve that compatibility,
        // but never erase an explicit non-game (or malformed) discriminator.
        if (Text(work, "object") != "work" || !ValidId(id)
            || Field(work, "medium").ValueKind != JsonValueKind.Undefined && Text(work, "medium") != "galgame")
            throw new OnlineException(OnlineFailure.InvalidResponse);
        var title = Localized(work); if (title.Length == 0) title = "作品 " + id;
        var original = Text(work, "display_name"); if (original.Length == 0) original = title;
        var titles = Rows(work, "titles").ToArray();
        var evidence = new Dictionary<(string Text, string Kind), TitleEvidence>();
        void Add(string name, string kind, bool machine, string? language)
        {
            if (name.Length is 0 or >= 500) return;
            var key = (name, kind);
            if (evidence.TryGetValue(key, out var old)) evidence[key] = old with { IsMachine = old.IsMachine || machine, Language = old.Language == language ? language : null };
            else if (evidence.Count < 499) evidence.Add(key, new(name, kind, machine, language));
        }
        Add(Text(work, "latin"), "latin", false, null);
        foreach (var row in titles)
        {
            var kind = Text(row, "title_kind"); if (kind is not ("official" or "alias" or "abbreviation")) kind = "unknown";
            var language = Text(row, "lang"); string? lang = language.Length is > 0 and <= 32 ? language : null;
            bool machine = Field(row, "is_machine").ValueKind != JsonValueKind.False;
            Add(Text(row, "title"), kind, machine, lang); Add(Text(row, "latin"), kind, machine, lang);
        }
        string? Allowed(JsonElement cover) => !includeExplicit && Text(cover, "sexual") == "explicit" ? null : Image(cover);
        // Unclassified gallery rows can be discs, soundtracks, back scans or books.
        // Only the declared primary image is an automatic catalog fallback.
        var coverUrl = Allowed(Field(work, "cover"));
        ImmutableArray<WorkCover>? covers = null;
        var coverRows = Field(work, "covers");
        if (coverRows.ValueKind != JsonValueKind.Undefined)
        {
            if (coverRows.ValueKind != JsonValueKind.Array || coverRows.GetArrayLength() > 500) throw new OnlineException(OnlineFailure.InvalidResponse);
            var seenIds = new HashSet<string>(); var seenUrls = new HashSet<string>(); var builder = ImmutableArray.CreateBuilder<WorkCover>();
            foreach (var c in coverRows.EnumerateArray())
            {
                var cid = Text(c, "id"); var url = Allowed(c); var sexual = Text(c, "sexual");
                if (!ValidId(cid) || Field(c, "id").GetString() != cid || url is null || url.Length > 8192 || seenIds.Contains(cid) || seenUrls.Contains(url)) continue;
                seenIds.Add(cid); seenUrls.Add(url);
                builder.Add(new(cid, url, Dimension(Field(c, "width")), Dimension(Field(c, "height")), Integer(Field(c, "vote_count")), sexual is "safe" or "suggestive" or "explicit" ? sexual : null));
            }
            covers = builder.ToImmutable();
        }
        coverUrl = CoverQuality.Select(covers ?? [], coverUrl, includeExplicit);
        var references = Rows(work, "refs").Where(r => Text(r, "source").Length > 0 && Text(r, "external_id").Length > 0)
            .Select(r => new WorkReference(Text(r, "source"), Text(r, "external_id"), ReferenceUrl(Text(r, "source"), Text(r, "external_id")))).ToList();
        var knownLinks = references.Select(r => r.Url).ToHashSet();
        foreach (var link in Rows(work, "links"))
            if (Text(link, "source") is { Length: > 0 } source && Uri.TryCreate(Text(link, "url"), UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.IsDefaultPort && ReferenceHosts.Contains(uri.Host) && knownLinks.Add(uri.AbsoluteUri)) references.Add(new(source, uri.AbsoluteUri, uri.AbsoluteUri));
        var ratings = ImmutableArray.CreateBuilder<SourceRating>();
        foreach (var r in Rows(work, "ratings").OrderBy(r => Text(r, "source") == "nextmoe" ? 0 : 1))
        {
            var source = Text(r, "source"); double scale = source switch { "nextmoe" or "vndb" or "bangumi" => 10, "erogamescape" or "howlongtobeat" => 100, "dlsite" => 5, _ => 0 };
            var score = Number(Field(r, "score")); if (scale > 0 && score >= 0 && score <= scale) ratings.Add(new(source, score.Value, scale, Integer(Field(r, "vote_count"))));
        }
        var intros = Rows(work, "intros").Where(i => Text(i, "value").Length > 0).ToArray();
        var intro = CatalogDisplayDto.PreferredIntro(intros);
        var date = Text(work, "release_date");
        return new(id, title, original, Unique(new[] { Text(work, "latin") }.Concat(titles.SelectMany(t => new[] { Text(t, "title"), Text(t, "latin") })).Where(s => s != title && s != original)),
            [.. evidence.Values], coverUrl, covers, Image(Field(work, "banner")), Text(intro, "value"), DatePattern().IsMatch(date) ? date : null, NullText(work, "release_date_precision"),
            Unique(Rows(work, "companies").Where(c => Text(c, "attribution_role") == "developer").Select(Localized)),
            Unique(Rows(work, "tags").Where(t => Text(t, "tier") != "hidden" && Text(t, "spoiler") is "" or "none").Select(Localized)), ratings.ToImmutable(),
            CatalogDisplayDto.Characters(work, includeExplicit).Where(c=>c.Spoiler is "" or "none").Select(c=>new WorkCharacter(c.Entity.Id,c.Entity.Name,c.OriginalName,c.Entity.ImageUrl,c.Role,[..c.Voices.Select(v=>v.Name)])).ToImmutableArray(),
            [.. references], "https://www.kungal.com/galgame/" + id, NullText(work, "content_rating"), now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))
        {
            Presentation = CatalogDisplayDto.Work(work, includeExplicit, Text(intro, "source")),
            CatalogIdentity = new(
                Unique(Rows(work, "companies").Take(64).Where(c => Text(c, "attribution_role") is "developer" or "publisher")
                    .SelectMany(c => new[] { Localized(c), Text(c, "display_name") }).Where(n => n.Length <= 100)).Take(32).ToImmutableArray(),
                Unique(Rows(work, "links").Take(64).Where(l => Text(l, "source") == "official_site").Select(l => Text(l, "url"))
                    .Where(u => u.Length <= 2048 && Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.IsDefaultPort))
                    .Take(32).ToImmutableArray())
        };
    }
    private static string? NullText(JsonElement value, string field) => Text(value, field) is { Length: > 0 } text ? text : null;
    [GeneratedRegex("^[0-9]{1,20}$")] private static partial Regex IdPattern();
    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$")] private static partial Regex DatePattern();
}
