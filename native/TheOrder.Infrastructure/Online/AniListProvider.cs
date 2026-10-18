using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

public sealed class AniListProvider(HttpClient client, AniListRateCoordinator? rate = null,
    TimeSpan? operationTimeout = null, Func<DateTimeOffset>? clock = null) : IAniListProvider
{
    public const string Endpoint = "https://graphql.anilist.co";
    public const string UserAgent = "TheOrder/1.1.0 (https://github.com/Ph-Rank/The-Order)";
    private const string Fields = "id idMal type title { romaji english native } synonyms format status coverImage { large } startDate { year month day } endDate { year month day } episodes chapters volumes";
    private const string People = "characters(page:$page,perPage:24,sort:[ROLE,ID]) { pageInfo { hasNextPage } edges { role node { id name { full native } image { large } } voiceActors(language:JAPANESE) { id name { full native } image { large } } voiceActorRoles(language:JAPANESE) { voiceActor { id name { full native } image { large } } roleNotes dubGroup } } } staff(page:$page,perPage:32,sort:[RELEVANCE,ID]) { pageInfo { hasNextPage } edges { role node { id name { full native } image { large } } } }";
    public static string DetailQuery => "query($id:Int!,$type:MediaType!,$page:Int!){ Media(id:$id,type:$type){" + Fields + " description(asHtml:false) genres tags { name isGeneralSpoiler isMediaSpoiler } studios(isMain:true){ nodes { name } } externalLinks { id url site type language notes isDisabled } trailer { id site } " + People + " relations { edges { relationType node { " + Fields + " } } } }}";
    private DateTimeOffset Now => (clock ?? (() => DateTimeOffset.UtcNow))();
    public async Task<AniListMedia[]> SearchAsync(AniListSource source, string? query, CancellationToken cancellation)
        => (await SearchCandidatesAsync(source, query, cancellation).ConfigureAwait(false)).Items;
    public async Task<AniListSearchCandidates> SearchCandidatesAsync(AniListSource source, string? query, CancellationToken cancellation)
    {
        if (!source.IsValid || query is not null && !AniListValidation.Text(query, 2000, false)) throw new InvalidDataException("检索条件无效。");
        if (AniListReference.TryParse(query, out var reference)) return new([await ResolveAsync(reference!, cancellation).ConfigureAwait(false)], true);
        var titles = query is not null ? new[] { query } : new[] { source.OriginalTitle, source.Title }.Where(x => x.Length > 0).Distinct().Take(2).ToArray();
        var found = new Dictionary<int, AniListMedia>(); bool complete = true;
        foreach (var title in titles)
        {
            JsonDocument json;
            try
            {
                json = await QueryAsync("query($search:String!,$type:MediaType!){ Page(page:1,perPage:10){ pageInfo { hasNextPage } media(search:$search,type:$type){" + Fields + "} }}",
                    new { search = title, type = Type(source.Kind) }, cancellation).ConfigureAwait(false);
            }
            catch (AniListException e) when (found.Count > 0 && e.Failure is AniListFailure.Offline or AniListFailure.Timeout or AniListFailure.RateLimited or AniListFailure.Server)
            { cancellation.ThrowIfCancellationRequested(); complete = false; break; }
            using (json)
            {
            if (!json.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("Page", out var page) || !page.TryGetProperty("media", out var rows) || rows.ValueKind != JsonValueKind.Array) throw Invalid();
            complete &= page.TryGetProperty("pageInfo", out var info) && info.ValueKind == JsonValueKind.Object
                && info.TryGetProperty("hasNextPage", out var next) && next.ValueKind == JsonValueKind.False
                && rows.GetArrayLength() <= 10;
            foreach (var row in rows.EnumerateArray().Take(10)) { var media = MapMedia(row); if (media.Kind != source.Kind) throw Invalid(); found.TryAdd(media.Id, media); }
            // Both native and display-title queries contribute evidence. A
            // truncated page stays useful for manual choice, never uniqueness.
            }
        }
        return new(found.Values.Take(20).ToArray(), complete && found.Count <= 20);
    }
    public async Task<AniListMedia> ResolveAsync(AniListReference reference, CancellationToken cancellation)
    {
        if (reference.Id <= 0 || !Enum.IsDefined(reference.Kind)) throw new InvalidDataException("AniList 编号无效。");
        var argument = reference.MyAnimeList ? "idMal" : "id";
        using var json = await QueryAsync("query($id:Int!,$type:MediaType!){ Media(" + argument + ":$id,type:$type){" + Fields + "} }",
            new { id = reference.Id, type = Type(reference.Kind) }, cancellation).ConfigureAwait(false);
        var media = MapMedia(MediaNode(json));
        if (media.Kind != reference.Kind || (reference.MyAnimeList ? media.MalId != reference.Id : media.Id != reference.Id)) throw Invalid();
        return media;
    }
    public async Task<AniListDetails> FetchAsync(int id, AniListMediaKind kind, int peoplePage, CancellationToken cancellation)
    {
        if (id <= 0 || !Enum.IsDefined(kind) || peoplePage is < 1 or > 10) throw new InvalidDataException("AniList 作品编号或分页无效。");
        using var json = await QueryAsync(DetailQuery, new { id, type = Type(kind), page = peoplePage }, cancellation).ConfigureAwait(false);
        var result = MapDetails(MediaNode(json), peoplePage, Now);
        if (result.Media.Id != id || result.Media.Kind != kind) throw Invalid(); return result;
    }
    private async Task<JsonDocument> QueryAsync(string query, object variables, CancellationToken cancellation)
    {
        if (client.DefaultRequestHeaders.Authorization is not null || client.DefaultRequestHeaders.Contains("Cookie")) throw Invalid();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(operationTimeout ?? TimeSpan.FromSeconds(25)); var token = timeout.Token;
        var coordinator = rate ?? AniListRateCoordinator.Shared; bool limited = false;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var lease = await coordinator.EnterAsync(token).ConfigureAwait(false);
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(new { query, variables }) };
                request.Headers.UserAgent.ParseAdd(UserAgent); request.Headers.Accept.ParseAdd("application/json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (response.RequestMessage?.RequestUri is { } actual && actual != request.RequestUri) throw Invalid();
                static int? Header(HttpResponseMessage r, string name) => r.Headers.TryGetValues(name, out var values) && int.TryParse(values.FirstOrDefault(), out var value) ? value : null;
                var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - Now);
                DateTimeOffset? reset = Header(response, "X-RateLimit-Reset") is { } stamp && stamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(stamp) : null;
                var status = (int)response.StatusCode;
                if (status == 429) { limited = true; retry ??= TimeSpan.FromMinutes(1); }
                coordinator.Observe(Header(response, "X-RateLimit-Limit"), Header(response, "X-RateLimit-Remaining"), reset, retry);
                if (status == 429 && attempt == 0 && retry is { } wait && wait.TotalSeconds <= 10) continue;
                if (status != 200) throw new AniListException(status switch { 429 => AniListFailure.RateLimited, >= 500 or 403 => AniListFailure.Server, 404 => AniListFailure.Unavailable, _ => AniListFailure.InvalidResponse },
                    status == 429 ? "AniList 请求较多，稍后可重试；Bangumi 资料仍可浏览。" : "AniList 暂时不可用，已保留 Bangumi 资料。");
                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (contentType is null || !contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)) throw Invalid();
                var bytes = await CatalogTransport.ReadBoundedAsync(response.Content, 3 * 1024 * 1024, token).ConfigureAwait(false);
                _ = new UTF8Encoding(false, true).GetCharCount(bytes);
                var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 });
                if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                {
                    int? errorStatus = errors.EnumerateArray().Select(x => Number(x, "status")).FirstOrDefault(x => x is not null);
                    document.Dispose();
                    if (errorStatus == 429) coordinator.Observe(null, 0, null, TimeSpan.FromMinutes(1));
                    throw new AniListException(errorStatus switch { 404 => AniListFailure.Unavailable, 429 => AniListFailure.RateLimited, 403 => AniListFailure.Server, _ => AniListFailure.InvalidResponse }, "AniList 未能提供此作品资料，Bangumi 资料仍可浏览。");
                }
                if (token.IsCancellationRequested) { document.Dispose(); token.ThrowIfCancellationRequested(); } return document;
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new AniListException(limited ? AniListFailure.RateLimited : AniListFailure.Timeout, limited ? "AniList 正在限流，请稍后重试。" : "AniList 读取超时，Bangumi 资料仍可浏览。"); }
        catch (HttpRequestException) { throw new AniListException(AniListFailure.Offline, "无法连接 AniList，Bangumi 资料仍可浏览。"); }
        catch (OnlineException) { throw new AniListException(AniListFailure.ResponseTooLarge, "AniList 响应超过可读取范围。"); }
        catch (Exception e) when (e is JsonException or DecoderFallbackException or IOException) { throw Invalid(); }
    }
    private static string Type(AniListMediaKind kind) => kind == AniListMediaKind.Anime ? "ANIME" : "MANGA";
    private static JsonElement MediaNode(JsonDocument document) => document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("Media", out var value) && value.ValueKind == JsonValueKind.Object
        ? value : throw new AniListException(AniListFailure.Unavailable, "AniList 没有此作品，仍可查看 Bangumi 资料。");
    public static AniListMedia MapMedia(JsonElement row) => MapMedia(row, allowUnknownRelationFormat: false);
    private static AniListMedia MapMedia(JsonElement row, bool allowUnknownRelationFormat)
    {
        var kind = Str(row, "type") switch { "ANIME" => AniListMediaKind.Anime, "MANGA" => AniListMediaKind.Manga, _ => throw Invalid() };
        var titles = Node(row, "title"); var native = Str(titles, "native"); var english = Str(titles, "english"); var romaji = Str(titles, "romaji");
        var result = new AniListMedia(Number(row, "id") ?? 0, Number(row, "idMal"), kind, english.Length > 0 ? english : romaji.Length > 0 ? romaji : native,
            native, english, Str(row, "format"), Str(row, "status"), Image(Node(row, "coverImage"), "large"), Date(Node(row, "startDate")), Date(Node(row, "endDate")),
            Positive(row, "episodes"), Positive(row, "chapters"), Positive(row, "volumes"), Rows(row, "synonyms").Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length > 0).Take(30).ToArray())
            { RomajiTitle = romaji.Length > 0 ? romaji : null };
        // Announced related works can legitimately have format:null. Keep the
        // typed relation without making unknown format a valid primary match.
        // Other malformed format values still follow the strict validation.
        var unknownRelationFormat = allowUnknownRelationFormat && row.TryGetProperty("format", out var format)
            && format.ValueKind == JsonValueKind.Null;
        if (!(unknownRelationFormat ? AniListValidation.RelationMedia(result) : AniListValidation.Media(result))) throw Invalid(); return result;
    }
    public static AniListDetails MapDetails(JsonElement row, int page, DateTimeOffset fetched)
    {
        var characters = Node(row, "characters"); var staff = Node(row, "staff");
        var result = new AniListDetails(MapMedia(row), Plain(Str(row, "description")), Rows(Node(row, "studios"), "nodes").Select(x => Str(x, "name")).Where(x => x.Length > 0).Distinct().Take(40).ToArray(),
            Rows(row, "genres").Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length > 0).Take(50).ToArray(),
            Rows(row, "tags").Where(x => !Flag(x, "isGeneralSpoiler") && !Flag(x, "isMediaSpoiler")).Select(x => Str(x, "name")).Where(x => x.Length > 0).Distinct().Take(100).ToArray(),
            Rows(characters, "edges").Take(24).Select(MapCharacter).ToArray(),
            Rows(staff, "edges").Take(32).Select(x => new AniListStaff(Person(Node(x, "node")), Str(x, "role"))).ToArray(),
            Rows(Node(row, "relations"), "edges").Take(100).Where(x => Str(Node(x, "node"), "type") is "ANIME" or "MANGA").Select(x => new AniListRelation(MapMedia(Node(x, "node"), allowUnknownRelationFormat: true), Str(x, "relationType"))).ToArray(),
            page, Flag(Node(characters, "pageInfo"), "hasNextPage"), Flag(Node(staff, "pageInfo"), "hasNextPage"), fetched)
        {
            WatchLinks = Rows(row, "externalLinks").Where(x => Str(x, "type") == "STREAMING" && !Flag(x, "isDisabled"))
                .Select(x => new AniListWatchLink(Number(x, "id") ?? 0, Str(x, "site"), Str(x, "url"), Optional(x, "language"), Optional(x, "notes")))
                .Where(AniListPlayback.ValidWatchLink).DistinctBy(x => x.Url).Take(24).ToArray(),
            Trailer = MapTrailer(Node(row, "trailer")), HasPlaybackMetadata = true,
            BookLinks = MapBookLinks(row),
            HasBookLinkMetadata = Str(row, "type") == "MANGA" && row.TryGetProperty("externalLinks", out _)
        };
        if (!AniListValidation.Details(result)) throw Invalid(); return result;
    }
    public static BookExternalLink[] MapBookLinks(JsonElement row) => Str(row, "type") == "MANGA"
        ? Rows(row, "externalLinks").Take(96).Select(x => new BookExternalLink(Number(x, "id") ?? 0, Str(x, "site"), Optional(x, "url"), Str(x, "type"),
            Optional(x, "language"), Optional(x, "notes"), Flag(x, "isDisabled"))).Where(BookOutlets.ValidRaw).ToArray() : [];
    private static AniListPerson Person(JsonElement row) => new(Number(row, "id") ?? 0, Str(Node(row, "name"), "full"), Str(Node(row, "name"), "native"), Image(Node(row, "image"), "large"));
    private static AniListCharacter MapCharacter(JsonElement row) => new(Person(Node(row, "node")), Str(row, "role"), Rows(row, "voiceActors").Take(8).Select(Person).ToArray())
    {
        VoiceRoles = Rows(row, "voiceActorRoles").Take(8).Select(x => new AniListVoiceRole(Person(Node(x, "voiceActor")), Optional(x, "roleNotes"), Optional(x, "dubGroup")))
            .DistinctBy(x => (x.Person.Id, x.RoleNotes, x.DubGroup)).ToArray()
    };
    private static string? Optional(JsonElement row, string key) => Str(row, key) is { Length: > 0 } value ? value : null;
    private static AniListTrailer? MapTrailer(JsonElement row)
    {
        var trailer = new AniListTrailer(Str(row, "site").ToLowerInvariant(), Str(row, "id"));
        return AniListPlayback.ValidTrailer(trailer) ? trailer : null;
    }
    private static string Plain(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, @"<br\s*/?>|</p>", "\n", RegexOptions.IgnoreCase), "<[^>]*>", "")).Replace("\r\n", "\n"), @"\n[\t ]*\n(?:[\t ]*\n)+", "\n\n").Trim();
    private static string? Image(JsonElement row, string key) => Str(row, key) is { Length: > 0 } value && AniListValidation.Image(value) ? value : null;
    private static string? Date(JsonElement node)
    {
        var year = Number(node, "year"); if (year is not (> 0 and <= 9999)) return null;
        var value = year.Value.ToString("D4", CultureInfo.InvariantCulture);
        if (Number(node, "month") is >= 1 and <= 12 and var month) { value += "-" + month.ToString("D2", CultureInfo.InvariantCulture); if (Number(node, "day") is >= 1 and <= 31 and var day) value += "-" + day.ToString("D2", CultureInfo.InvariantCulture); }
        return AniListValidation.Date(value) ? value : null;
    }
    private static int? Positive(JsonElement node, string key) => Number(node, key) is > 0 and var value ? value : null;
    private static int? Number(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
    private static string Str(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    private static JsonElement Node(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;
    private static IEnumerable<JsonElement> Rows(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
    private static bool Flag(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static AniListException Invalid() => new(AniListFailure.InvalidResponse, "AniList 返回的身份或资料格式不一致，保留原有资料。");
}
