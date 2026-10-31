using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Domain;
using Xunit;

namespace TheOrder.Tests;

public sealed partial class PlaygroundCoreTests
{
    private sealed class NativeSearchProvider(PlaygroundProviderKind kind = PlaygroundProviderKind.OpenAI) : IPlaygroundProvider
    {
        public PlaygroundProviderKind Kind { get; } = kind;
        public string DefaultEndpoint => PlaygroundEndpointPolicy.Default(Kind);
        public readonly List<PlaygroundProviderRequest> Requests = [];
        public readonly List<PlaygroundPublicSearchRequest> Searches = [];
        public readonly List<PlaygroundPublicFetchRequest> Fetches = [];
        public Func<PlaygroundProviderRequest, IEnumerable<PlaygroundProviderEvent>> Reply = _ => Answer("当前讨论");
        public PlaygroundResearchResult Research = new("有出处的公开资料", [new("public_web", PlaygroundSourceKind.Web, "公开原文") { Url = "https://example.org/official" }]);
        public PlaygroundCapabilities Capabilities(string model, string endpoint) => new(true, true, true, true) { ReasoningEfforts = ["high"] };
        public Task<ImmutableArray<PlaygroundModel>> ListModelsAsync(string endpoint, CancellationToken cancellation) => Task.FromResult(ImmutableArray<PlaygroundModel>.Empty);
        public async IAsyncEnumerable<PlaygroundProviderEvent> SendAsync(PlaygroundProviderRequest request, [EnumeratorCancellation] CancellationToken cancellation)
        { Requests.Add(request); await Task.Yield(); foreach (var item in Reply(request)) { cancellation.ThrowIfCancellationRequested(); yield return item; } }
        public Task<PlaygroundResearchResult> SearchAsync(PlaygroundPublicSearchRequest request, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Searches.Add(request); return Task.FromResult(Research); }
        public Task<PlaygroundResearchResult> FetchAsync(PlaygroundPublicFetchRequest request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Fetches.Add(request);
            return Task.FromResult(new PlaygroundResearchResult("逐页读取的公开正文", [new("fetched_web", PlaygroundSourceKind.Web, "公开原文") { Url = request.Url }], new(Fetches: 1)));
        }
    }
    private sealed class NativeSearchRig : IAsyncDisposable
    {
        public readonly Store Store = new();
        public readonly NativeSearchProvider Provider;
        public readonly PlaygroundService Service;
        public LibrarySnapshot Library = new([Entry() with { Name = "PRIVATE-NAME-CANARY", Notes = "PRIVATE-NOTE-CANARY" }], LibraryRecovery.Main);
        public NativeSearchRig(PlaygroundProviderKind kind = PlaygroundProviderKind.OpenAI)
        { Provider = new(kind); Service = new(Store, new Credentials(), [Provider], () => Library); }
        public async Task<Guid> Ready(PlaygroundPermissions? permissions = null, PlaygroundSearchMode search = PlaygroundSearchMode.Smart,
            PlaygroundSpoilerMode spoiler = PlaygroundSpoilerMode.Full)
        {
            await Service.InitializeAsync();
            await Service.ConfigureAsync(new() { Enabled = true, Permissions = permissions ?? new(), Connections = [new(Provider.Kind, Provider.DefaultEndpoint, "fixture", true)] });
            var id = await Service.CreateConversationAsync();
            await Service.UpdateConversationAsync(id, Service.Snapshot.Conversations.Single(c => c.Id == id).Options with { Search = search, Spoiler = spoiler });
            return id;
        }
        public PlaygroundMessage Last => Service.Snapshot.Messages.Last(m => m.Role == "assistant");
        public ValueTask DisposeAsync() => Service.DisposeAsync();
    }

    [Fact]
    public async Task NativePublicSearchKeepsSpecificQuestionButNeverImportsPrivateMaterials()
    {
        await using var r = new NativeSearchRig();
        var id = await r.Ready(new(true, true, true, true), PlaygroundSearchMode.Off);
        await r.Service.SendAsync(id, "我喜欢剧情 PRIVATE-HISTORY-CANARY", [new("notes.txt", "PRIVATE-FILE-CANARY")]);
        var options = r.Service.Snapshot.Conversations.Single(c => c.Id == id).Options;
        await r.Service.UpdateConversationAsync(id, options with { Search = PlaygroundSearchMode.Smart });
        r.Provider.Reply = request => ToolAnswer(request, "public_search", new { }, "已核对公开资料");
        await r.Service.SendAsync(id, "我觉得里奈线艺术性最强。请核实《樱之诗》与《樱之刻》特典的名称、载体和官方阅读顺序；不要透露续作剧情。我的笔记 PRIVATE-CURRENT-CANARY，游戏路径 D:\\Private-CANARY。");
        var query = Assert.Single(r.Provider.Searches).Query;
        Assert.Contains("樱之诗", query); Assert.Contains("樱之刻", query); Assert.Contains("载体", query); Assert.Contains("官方阅读顺序", query);
        Assert.DoesNotContain("PRIVATE", query); Assert.DoesNotContain("我觉得", query); Assert.DoesNotContain("里奈", query); Assert.DoesNotContain("D:\\", query);
        Assert.DoesNotContain("公式 作品 資料 出典", query);
        Assert.All(r.Provider.Requests, request => Assert.Equal(PlaygroundPhase.Discussion, request.Phase));
        Assert.Contains("PRIVATE-HISTORY-CANARY", string.Join("\n", r.Provider.Requests.SelectMany(x => x.Messages).Select(x => x.Text)));
    }

    [Fact]
    public async Task NativePublicSearchCanBeSelectedByModelAndDoesNotRepeatSamePaidResearch()
    {
        await using var r = new NativeSearchRig(); var id = await r.Ready();
        r.Provider.Reply = request =>
        {
            if (r.Provider.Requests.Count < 3)
                return [new(PlaygroundProviderEventKind.Continuation) { Continuation = new(request.Binding, "synthetic-tool-state") },
                    new(PlaygroundProviderEventKind.ToolCall) { ToolCall = new("call-" + r.Provider.Requests.Count, "public_search", "{}") },
                    new(PlaygroundProviderEventKind.Completed) { StopReason = "tool_calls" }];
            return Answer("语音差异见公开原文 [[public_web]]");
        };
        await r.Service.SendAsync(id, "比较《魔女的夜宴》原版和重制版的语音差异。");
        Assert.Equal(3, r.Provider.Requests.Count); Assert.Single(r.Provider.Searches);
        Assert.Contains("语音差异", r.Provider.Searches[0].Query);
        Assert.Contains(r.Provider.Requests[0].Tools, t => t.Name == "public_search" && !t.ParametersJson.Contains("\"query\""));
        Assert.DoesNotContain("没有搜索工具可调用", r.Provider.Requests[0].Instructions);
        Assert.Contains(r.Last.Sources, s => s.Id == "public_web"); Assert.Equal(PlaygroundMessageStatus.Complete, r.Last.Status);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("work_title")]
    [InlineData("notes")]
    public async Task NativePublicSearchRejectsModelSuppliedPrivateQueryFields(string field)
    {
        await using var r = new NativeSearchRig(); var id = await r.Ready(new(true, true, true, false));
        r.Provider.Reply = request => ToolAnswer(request, "public_search", new Dictionary<string, string> { [field] = "PRIVATE-TOOL-CANARY" }, "未发送私人检索词");
        await r.Service.SendAsync(id, "比较《魔女的夜宴》的语音差异。");
        Assert.Empty(r.Provider.Searches);
        Assert.Contains("invalid_arguments", r.Provider.Requests.Last().ToolResults.Single().Text);
    }

    [Theory]
    [InlineData(PlaygroundSearchMode.Off, PlaygroundSpoilerMode.Full, "请联网核实《魔女的夜宴》的发行资料。")]
    [InlineData(PlaygroundSearchMode.Smart, PlaygroundSpoilerMode.Full, "我以前给《魔女的夜宴》打了什么评分？")]
    [InlineData(PlaygroundSearchMode.Smart, PlaygroundSpoilerMode.Strict, "请搜索《魔女的夜宴》的结局真相。")]
    public async Task NativePublicSearchAndFetchAreBothAbsentOutsideCurrentPublicScope(PlaygroundSearchMode mode, PlaygroundSpoilerMode spoiler, string question)
    {
        await using var r = new NativeSearchRig(PlaygroundProviderKind.DeepSeek); var id = await r.Ready(new(true, true, true, false), mode, spoiler);
        r.Provider.Reply = request => ToolAnswer(request, "public_fetch", new { source_id = "public_web" }, "没有发送网页请求");
        await r.Service.SendAsync(id, question);
        Assert.Empty(r.Provider.Searches); Assert.Empty(r.Provider.Fetches);
        Assert.DoesNotContain(r.Provider.Requests[0].Tools, t => t.Name is "public_search" or "public_fetch");
    }

    [Theory]
    [InlineData("public_web", true)]
    [InlineData("record_private", false)]
    [InlineData("https://example.org/arbitrary", false)]
    public async Task NativePublicFetchUsesRegisteredSourceIdentityOnly(string sourceId, bool permitted)
    {
        await using var r = new NativeSearchRig(PlaygroundProviderKind.DeepSeek); var id = await r.Ready();
        r.Provider.Reply = request => r.Provider.Requests.Count < 3
            ? [new(PlaygroundProviderEventKind.Continuation) { Continuation = new(request.Binding, "synthetic-search-fetch") },
                new(PlaygroundProviderEventKind.ToolCall) { ToolCall = r.Provider.Requests.Count == 1
                    ? new("search", "public_search", "{}") : new("fetch", "public_fetch", JsonSerializer.Serialize(new { source_id = sourceId })) },
                new(PlaygroundProviderEventKind.Completed) { StopReason = "tool_calls" }]
            : Answer("已检查读取范围");
        await r.Service.SendAsync(id, "请核实《魔女的夜宴》的发售资料。");
        Assert.Single(r.Provider.Searches);
        Assert.Equal(permitted ? 1 : 0, r.Provider.Fetches.Count);
        if (permitted) Assert.Equal("https://example.org/official", r.Provider.Fetches.Single().Url);
        else Assert.Contains("permission_denied", r.Provider.Requests.Last().ToolResults.Single().Text);
    }

    [Fact]
    public async Task NativePublicGoogleToolDisplaysFullAnswerWithoutReturningGroundedBodyToDiscussion()
    {
        await using var r = new NativeSearchRig(PlaygroundProviderKind.Gemini); var id = await r.Ready();
        string grounded = "GROUNDED-CANARY" + new string('文', 120000);
        r.Provider.Research = r.Provider.Research with { Text = grounded, Retention = PlaygroundRetention.SessionOnly, SearchSuggestionsHtml = "<div>fixture suggestions</div>" };
        r.Provider.Reply = request => ToolAnswer(request, "public_search", new { }, "不得到达的第二次综合");
        await r.Service.SendAsync(id, "比较《魔女的夜宴》原版和重制版的语音差异。");
        Assert.Single(r.Provider.Requests); Assert.Single(r.Provider.Searches);
        Assert.Equal(grounded, r.Last.Text); Assert.Equal(PlaygroundMessageStatus.Complete, r.Last.Status);
        Assert.Equal(PlaygroundRetention.SessionOnly, r.Last.Retention); Assert.Contains("suggestions", r.Last.SearchSuggestionsHtml);
        Assert.All(r.Last.Sources, source => Assert.Equal(PlaygroundSourceKind.Web, source.Kind));
        Assert.All(r.Store.Writes, write => Assert.DoesNotContain("GROUNDED-CANARY", JsonSerializer.Serialize(write)));
        Assert.Empty(r.Service.Snapshot.Memories);
        r.Provider.Reply = _ => Answer("继续当前讨论");
        await r.Service.SendAsync(id, "继续讨论。");
        Assert.DoesNotContain("GROUNDED-CANARY", string.Join("\n", r.Provider.Requests.Last().Messages.Select(m => m.Text)));
    }

    [Fact]
    public async Task NativePublicGoogleOversizedAnswerIsExplicitlyInterrupted()
    {
        await using var r = new NativeSearchRig(PlaygroundProviderKind.Gemini); var id = await r.Ready();
        await r.Service.ConfigureAsync(r.Service.Snapshot.Settings with { MaximumOutputTokens = 128 });
        r.Provider.Research = r.Provider.Research with { Text = new string('文', 3000), Retention = PlaygroundRetention.SessionOnly };
        r.Provider.Reply = request => ToolAnswer(request, "public_search", new { }, "不可二次综合");
        await r.Service.SendAsync(id, "请核实《魔女的夜宴》的发售资料。");
        Assert.Equal(PlaygroundMessageStatus.Interrupted, r.Last.Status); Assert.Equal(2048, r.Last.Text.Length);
        Assert.Contains("仅显示部分", r.Last.Error); Assert.Single(r.Provider.Requests);
    }

    [Fact]
    public async Task NativePublicPriorPortableCitationIsNotReadmittedAfterSearchIsTurnedOff()
    {
        await using var r = new NativeSearchRig(); var id = await r.Ready();
        r.Provider.Reply = request => ToolAnswer(request, "public_search", new { }, "第一轮已核对 [[public_web]]");
        await r.Service.SendAsync(id, "请核实《魔女的夜宴》的发售资料。");
        Assert.Contains(r.Last.Sources, s => s.Id == "public_web");
        var options = r.Service.Snapshot.Conversations.Single(c => c.Id == id).Options;
        await r.Service.UpdateConversationAsync(id, options with { Search = PlaygroundSearchMode.Off });
        r.Provider.Reply = _ => Answer("沿用此前资料 [[public_web]]");
        await r.Service.SendAsync(id, "继续比较刚才的资料。");
        Assert.Single(r.Provider.Searches); Assert.DoesNotContain(r.Last.Sources, s => s.Id == "public_web");
        Assert.DoesNotContain("[[public_web]]", r.Last.Text); Assert.Contains("未核验引用", r.Last.Text);
        Assert.DoesNotContain("第一轮已核对", string.Join("\n", r.Provider.Requests.Last().Messages.Select(m => m.Text)));
    }

    [Theory]
    [InlineData("opaque-signature", false)]
    [InlineData("escaped-prose", false)]
    [InlineData("large-prose", true)]
    [InlineData("tool-json-text", true)]
    [InlineData("tool-arguments-object", true)]
    public async Task NativeEnvelopeBudgetSeparatesProtocolOpacityFromAllReadableToolText(string scenario, bool blocked)
    {
        await using var r = new NativeSearchRig(); var id = await r.Ready(new(false, false, true, false), PlaygroundSearchMode.Off);
        await r.Service.ConfigureAsync(r.Service.Snapshot.Settings with { MaximumContextCharacters = 8000 });
        object item = scenario switch
        {
            "opaque-signature" => new { type = "thought", text = "短正文", signature = new string('s', 30000) },
            "escaped-prose" => new { type = "model_output", content = new[] { new { type = "text", text = new string('文', 2400) } } },
            "large-prose" => new { type = "model_output", content = new[] { new { type = "text", text = new string('文', 12000) } } },
            "tool-json-text" => new { type = "function_result", result = JsonSerializer.Serialize(new { type = "thought", signature = new string('x', 12000) }) },
            _ => new { type = "function_call", name = "get_preferences", arguments = new { type = "thought", signature = new string('x', 12000) } }
        };
        string envelope = JsonSerializer.Serialize(new { items = new[] { item } });
        r.Provider.Reply = request => request.ToolResults.IsEmpty
            ? [new(PlaygroundProviderEventKind.Continuation) { Continuation = new(request.Binding, envelope) },
                new(PlaygroundProviderEventKind.ToolCall) { ToolCall = new("memory-read", "get_preferences", "{}") },
                new(PlaygroundProviderEventKind.Completed) { StopReason = "tool_calls" }]
            : Answer("完整正文");
        await r.Service.SendAsync(id, "讨论音乐。");
        Assert.Equal(blocked ? 1 : 2, r.Provider.Requests.Count);
        Assert.Equal(blocked ? PlaygroundMessageStatus.Failed : PlaygroundMessageStatus.Complete, r.Last.Status);
        if (blocked) Assert.Contains("字符预算", r.Last.Error);
    }
}
