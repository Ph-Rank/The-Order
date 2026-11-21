using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.WinUI.Startup;

// Explicit DIAGNOSTICS + ONLINE_FIXTURE opt-in only. The normal client never installs this handler.
// Exercises the real transport/parser/VM/image decoder against synthetic HTTP responses, without networking.
public sealed class SyntheticOnline : HttpMessageHandler, ICredentialProvider, ICredentialSettings
{
    public HttpClient Client { get; }
    private readonly bool slowConnection;
    private readonly bool recognitionFixture;
    public bool BoundaryFixture { get; }
    private TimeSpan clockOffset;
    private TaskCompletionSource? delayedCandidate;
    public DateTimeOffset Now => DateTimeOffset.UtcNow + clockOffset;
    public void DelayCandidate() { if (!BoundaryFixture) return; clockOffset += TimeSpan.FromMinutes(10); delayedCandidate = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    public void ReleaseCandidate() { delayedCandidate?.TrySetResult(); delayedCandidate = null; }
    public int DetailRequests { get; private set; }
    public SyntheticOnline(bool slowConnection = false, bool recognitionFixture = false, bool boundaryFixture = false) { this.slowConnection = slowConnection; this.recognitionFixture = recognitionFixture; BoundaryFixture = recognitionFixture && boundaryFixture; Client = new(this, false) { Timeout = Timeout.InfiniteTimeSpan }; }
    protected override void Dispose(bool disposing) { if (disposing) Client.Dispose(); base.Dispose(disposing); }
    private bool saved = true;
    private bool failedScrollContinuation;
    public Task<string?> ReadAsync(CancellationToken cancellation) => Task.FromResult<string?>(saved ? "synthetic-only" : null);
    public Task<bool> HasCredentialAsync(CancellationToken cancellation) => Task.FromResult(saved);
    public Task SaveAsync(string secret, CancellationToken cancellation) { saved = true; return Task.CompletedTask; }
    public Task ClearAsync(CancellationToken cancellation) { saved = false; return Task.CompletedTask; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        var uri = request.RequestUri!;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Split('=', 2)).ToDictionary(v => Uri.UnescapeDataString(v[0]), v => v.Length > 1 ? Uri.UnescapeDataString(v[1]) : "");
        if (uri.Host == "images.example")
        {
            var image = uri.AbsolutePath.Contains("invalid", StringComparison.Ordinal) ? new byte[] { 1, 2, 3 } : Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAACgAAAA8CAIAAACb22+3AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAABUSURBVFhH7c2hFQAgDMTQA1HWY1gGRIPmBZOo9sxvMyuVVDJyHNf7eu/5lDCWMJYwljCWMJYwljCWMJYwljCWMJYwljCWMJYwljCWMJYwljDWN3gDzZMCdFL7NuoAAAAASUVORK5CYII=");
            if (uri.AbsolutePath.Contains("alternative", StringComparison.Ordinal)) image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAADwAAAAoCAYAAACiu5n/AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAACBSURBVGhD7c8xAYMAAMRA1FUJSuqhTmoLDbBHwLH8cHtynL/rfsPn+3/F0RClIcqGlYYoG1YaomxYaYiyYaUhyoaVhigbVhqibFhpiLJhpSHKhpWGKBtWGqJsWGmIsmGlIcqGlYYoG1YaomxYaYiyYaUhyoaVhigbVhqibFhpiPIAO6EzeKnXZYkAAAAASUVORK5CYII=");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) }; response.Content.Headers.ContentType = new("image/png"); return response;
        }
        if (Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test)
        {
            if (uri.AbsolutePath.StartsWith("/v2/catalog/works/", StringComparison.Ordinal)
                && SyntheticNextMoe.MergeTarget(uri.Segments[^1]) is { } target)
                return Json(new { @object = "work", code = "ENTITY_MERGED", current_id = target }, 404);
            if (uri.AbsolutePath == "/v2/catalog/works/900086" && Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PERSONAL_FIXTURE") == "1")
                return Json(new { @object = "work", code = "ENTITY_MERGED", current_id = "900090" }, 404);
            var more = query.ContainsKey("cursor");
            if (uri.AbsolutePath == "/v2/catalog/works" && query.TryGetValue("q", out var term) && term is "motion" or "motion-scroll" or "motion-retry")
            {
                await Task.Delay(550, cancellation);
                if (term == "motion-retry" && more && !failedScrollContinuation)
                {
                    failedScrollContinuation = true;
                    return Json(new { @object = "malformed-page" });
                }
                var ids = term != "motion"
                    ? Enumerable.Range(more ? 21 : 1, more ? 4 : 20).Select(id => id.ToString(CultureInfo.InvariantCulture))
                    : more ? new[] { "2", "3" } : new[] { "1", "2" };
                return Json(new { @object = "list", items = ids.Select(Work), next_cursor = more ? null : "cur_motion" });
            }
            if (uri.AbsolutePath == "/v2/catalog/rankings")
            {
                await Task.Delay(550, cancellation);
                var by = query["by"];
                return Json(new { @object = "list", by, metric = query["metric"], unit = by == "consensus" ? "deviation" : "native",
                    items = Enumerable.Range(more ? 3 : 1, 2).Select(rank => new
                    {
                        @object = "ranking_entry", rank, value = by switch
                        {
                            "consensus" or "erogamescape" or "howlongtobeat" => 91 - rank,
                            "dlsite" => 5m - rank / 10m,
                            _ => 9.1m - rank / 10m
                        },
                        work = Work(rank.ToString(CultureInfo.InvariantCulture))
                    }), next_cursor = more ? null : "cur_motion" });
            }
            if (uri.AbsolutePath == "/v2/news")
            {
                await Task.Delay(550, cancellation);
                return Json(new { @object = "list", items = Enumerable.Range(more ? 3 : 1, 2).Select(id => new
                    {
                        @object = "news_item", id = id.ToString(CultureInfo.InvariantCulture), title = $"合成资讯 {id}",
                        summary = "用于验证资讯加载和分页。", lane = query.GetValueOrDefault("lane", "news"),
                        source = new { display_name = "合成资讯来源", attribution = "合成转载署名" },
                        published_at = "2026-11-07T12:00:00Z"
                    }), next_cursor = more ? null : "cur_motion" });
            }
            if (Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_WALKTHROUGH_TEST") == "1")
            {
                const string listPath = "/v2/catalog/walkthroughs";
                if (uri.AbsolutePath == listPath && query.GetValueOrDefault("work_id") == "53")
                {
                    await Task.Delay(550, cancellation);
                    return Json(new { items = new[] { new { id = "900001", work_id = "53", source = "合成攻略", lang = "zh-Hans" } } });
                }
                if (uri.AbsolutePath == listPath + "/900001")
                {
                    await Task.Delay(550, cancellation);
                    return Json(new { id = "900001", work_id = "53", source = "合成攻略", lang = "zh-Hans", chart = new
                        {
                            sections = new[] { new { key = "main", title = "合成路线" } },
                            entries = new[] { new { key = "start", label = "START" } }, saves = Array.Empty<object>(),
                            endings = new[] { new { key = "end", name = "END" } },
                            segments = new[] { new { key = "route", section_key = "main",
                                from_node = new { node_type = "entry", key = "start" }, to_node = new { node_type = "ending", key = "end" },
                                steps = new[] { new { step_type = "choice", text = "沿着灯光继续前进" } } } }
                        } });
                }
            }
        }
        if (uri.AbsolutePath == "/v2/catalog/calendar" && query.TryGetValue("month", out var month)
            && Program.Profile?.Manifest.ProfileKind == NativeProfileKind.Test)
        {
            await Task.Delay(slowConnection ? 20_000 : 550, cancellation);
            return Json(new
            {
                @object = "list",
                items = Enumerable.Range(1, 100).Select(id => new
                {
                    @object = "work",
                    id = id.ToString(CultureInfo.InvariantCulture),
                    display_name = $"合成发售作品 {id}",
                    medium = "galgame",
                    content_limit = "sfw",
                    release_date = month + (id == 100 ? "-21" : "-20"),
                    release_date_precision = "day"
                }),
                meta = new { today = Now.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
            });
        }
        query.TryGetValue("q", out var q);
        if (recognitionFixture)
        {
            if (q?.Contains("離線", StringComparison.Ordinal) == true) throw new HttpRequestException("synthetic offline");
            if (q?.Contains("遅延", StringComparison.Ordinal) == true) await Task.Delay(20000, cancellation);
            if (uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal))
            {
                string[] ids = q?.Contains("双子", StringComparison.Ordinal) == true ? ["210", "211"] : q?.Contains("第2章", StringComparison.Ordinal) == true ? ["220"] : q?.Contains("星空", StringComparison.Ordinal) == true ? BoundaryFixture ? ["200", "201"] : ["200"] : [];
                return Json(new { @object = "list", items = ids.Select(id => new { @object = "search_result", target_object = "work", id }) });
            }
            if (query.TryGetValue("ids", out var idsQuery)) return Json(new { @object = "list", items = idsQuery.Split(',').Select(RecognitionWork) });
            if (query.ContainsKey("refs")) return Json(new { @object = "list", items = Array.Empty<object>() });
            if (uri.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal)) return Json(new { @object = "list", items = Array.Empty<object>() });
            if (query.ContainsKey("limit")) return Json(new { @object = "list", items = Array.Empty<object>() });
            DetailRequests++;
            string workId = uri.Segments.Last();
            // Deliberately return after cancellation to exercise real transport/Presentation rejection.
            if (workId == "200" && delayedCandidate is { } delay) await delay.Task;
            if (workId == "404") return Json(new { code = "NOT_FOUND" }, 404);
            return Json(RecognitionWork(workId == "86" ? "90" : workId));
        }
        if (q == "offline") throw new HttpRequestException("synthetic unavailable");
        if (q == "unauthorized") return Json(new { code = "EXPIRED_API_KEY" }, 401);
        if (q == "rate") { var response = Json(new { code = "RATE_LIMITED" }, 429); response.Headers.RetryAfter = new(TimeSpan.FromSeconds(60)); return response; }
        // Leave enough time for observable desktop interaction, still below the shared 45 s deadline.
        if (q is "slow" or "A") await Task.Delay(40000, cancellation);
        if (uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal))
        {
            string[] ids = q == "empty" ? [] : q == "invalid-cover" ? ["99"] : q == "mismatched-detail" ? ["86"] : q == "slow-detail" ? ["87"] : query.ContainsKey("cursor") ? ["2", "3"] : ["1", "2"];
            return Json(new { @object = "list", items = ids.Select(id => new { @object = "search_result", target_object = "work", id }), next_cursor = query.ContainsKey("cursor") || q is "empty" or "invalid-cover" ? null : "cur_page2" });
        }
        if (query.TryGetValue("ids", out var joined)) return Json(new { @object = "list", items = joined.Split(',').Where(id => Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test
            || SyntheticNextMoe.MergeTarget(id) is null).Reverse().Select(Work) });
        if (query.ContainsKey("limit")) { if (slowConnection) await Task.Delay(40000, cancellation); return Json(new { @object = "list", items = Array.Empty<object>() }); }
        DetailRequests++;
        var id = uri.Segments.Last();
        if (id == "87") await Task.Delay(40000, cancellation);
        return Json(Work(id == "86" ? "90" : id));
    }
    private static object Work(string id) => new { @object = "work", id, display_name = "合成在线作品 " + id, medium = "galgame", content_limit = "sfw",
        cover = new { url = "https://images.example/" + (id == "99" ? "invalid" : "cover") + ".png" },
        covers = ArtworkChoices(),
        intros = new[] { new { lang = "zh-Hans", value = "这是隔离验收使用的虚构作品。资料按需加载；加入愿望单后可离线查看与编辑。" } }, companies = new[] { new { attribution_role = "developer", display_name = "Synthetic Studio" } } };
    private static object RecognitionWork(string id) => new { @object = "work", id,
        display_name = id is "210" or "211" ? "双子の庭" : id == "220" ? "航路 第1章" : "星空の航路",
        cover = new { url = "https://images.example/cover.png" },
        covers = ArtworkChoices(),
        refs = new[] { new { source = "steam", external_id = id is "200" or "201" ? "123456" : id == "210" ? "210001" : id == "211" ? "211001" : "220001" } },
        intros = new[] { new { lang = "zh-Hans", value = "Phase 4B 虚构识别语料，候选编号 " + id + "，仅用于独立资料验收。" } },
        companies = new[] { new { attribution_role = "developer", display_name = "Synthetic Studio" } } };
    private static object[] ArtworkChoices() => [new { id = "1", url = "https://images.example/cover.png", width = 40, height = 60, vote_count = 0, sexual = "safe" },
        new { id = "2", url = "https://images.example/alternative.png", width = 60, height = 40, vote_count = 200, sexual = "safe" }];
    private static HttpResponseMessage Json(object value, int status = 200) => new((HttpStatusCode)status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}
