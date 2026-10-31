using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class WalkthroughTests
{
    private const string Synthetic = """
    {"id":"17","work_id":"198","source":"synthetic","source_url":"https://example.com/guide","lang":"ja","release":null,
      "ending_count":1,"save_count":1,"segment_count":2,"updated_at":"2026-10-29T00:00:00Z",
      "chart":{"sections":[{"key":"common","title":"共通"}],"entries":[{"key":"start","label":"START"}],
       "saves":[{"key":"save1","label":"SAVE 01"}],"endings":[{"key":"end1","name":"A END"}],
       "segments":[{"key":"route1","section_key":"common","from_node":{"node_type":"entry","key":"start"},
        "to_node":{"node_type":"ending","key":"end1"},"condition":null,"steps":[
         {"step_type":"save","text":null,"save_key":"save1","alternatives":[]},
         {"step_type":"choice","text":"手紙を読む","localized":{"zh-Hans":{"value":"读信","is_machine":true}},"save_key":null,
          "alternatives":[{"text":"待つ","localized":{"zh-Hans":{"value":"等待","is_machine":false}}}]}]},
        {"key":"route2","section_key":"common","from_node":{"node_type":"save","key":"save1"},
        "to_node":{"node_type":"ending","key":"end1"},"condition":"Second play","steps":[{"step_type":"action","text":"Continue","alternatives":[]}]}]}}
    """;
    public static WalkthroughDocument Document(string id = "17", string work = "198")
    {
        using var json = JsonDocument.Parse(Synthetic.Replace("\"17\"", "\"" + id + "\"").Replace("\"198\"", "\"" + work + "\""));
        return NextMoeWalkthroughs.Parse(json.RootElement, work);
    }
    [Fact] public void StructuredGuidePreservesOriginalTranslationSourceVersionAndSaveReferences()
    {
        var guide = Document(); Assert.Equal("未注明适用版本", guide.Summary.Release);
        Assert.Equal("https://example.com/guide", guide.Summary.SourceUrl);
        Assert.Equal("SAVE 01", guide.Segments[0].Steps[0].Text.Original);
        var choice = guide.Segments[0].Steps[1]; Assert.Equal("手紙を読む", choice.Text.Original);
        Assert.Equal("读信", choice.Text.Display); Assert.True(choice.Text.Machine);
        Assert.False(Assert.Single(choice.Alternatives).Machine); Assert.Equal("save1", guide.Segments[1].From.Key);
        var overlay = GuideOverlayDocument.From("local-game", "合成作品", guide);
        Assert.Equal("读信", overlay.Routes[0].Steps[1].Display);
        Assert.Equal("等待", Assert.Single(overlay.Routes[0].Steps[1].Alternatives).Display);
        Assert.Equal("save1", overlay.Routes[0].Steps[0].SaveKey);
        Assert.Contains("Second play", overlay.Routes[1].Title);
    }
    [Fact] public void UnlockRetainsTypedEndingKeyForTheFlowchart()
    {
        using var json = JsonDocument.Parse(Synthetic.Replace("\"label\":\"START\"", "\"label\":\"START\",\"unlocked_by_ending_key\":\"end1\""));
        var guide = NextMoeWalkthroughs.Parse(json.RootElement,"198");
        Assert.Equal("end1",guide.Segments[0].From.RequirementKey);
        Assert.Equal("A END",guide.Segments[0].From.Requirement);
        Assert.Single(WalkthroughFlowGraph.Build(guide).Edges.Where(e=>e.Kind=="unlock"));
    }
    [Theory] [InlineData("heroine",3)] [InlineData("normal",1)] [InlineData("bad",1)] [InlineData("after",0)] [InlineData("future-type",2)]
    public void ProviderEndingTypeIsPreservedWithoutGuessingFromTheName(string type,int expected)
    {
        using var json=JsonDocument.Parse(Synthetic.Replace("\"name\":\"A END\"","\"name\":\"A END\",\"ending_type\":\""+type+"\""));
        var doc=NextMoeWalkthroughs.Parse(json.RootElement,"198");Assert.Equal(type,doc.Segments[0].To.EndingType);
        var ending=WalkthroughFlowGraph.Build(doc).Nodes.Single(n=>n.Kind=="ending");Assert.Equal(type,ending.EndingType);
        Assert.Equal(expected,WalkthroughFlowGraph.EndingWeight(ending.Kind,ending.EndingType));
    }
    [Theory]
    [InlineData("\"work_id\":\"198\"", "\"work_id\":\"999\"")]
    [InlineData("\"key\":\"end1\"},\"condition\"", "\"key\":\"missing\"},\"condition\"")]
    [InlineData("https://example.com/guide", "file:///C:/private.txt")]
    [InlineData("https://example.com/guide", "https://user:secret@example.com/guide")]
    [InlineData("\"key\":\"route2\"", "\"key\":\"route1\"")]
    public void WrongIdentityDanglingReferencesUnsafeLinksAndDuplicateKeysAreRejected(string before, string after)
    {
        Assert.Contains(before, Synthetic); using var json = JsonDocument.Parse(Synthetic.Replace(before, after));
        var failure = Assert.Throws<OnlineException>(() => NextMoeWalkthroughs.Parse(json.RootElement, "198"));
        Assert.Equal(OnlineFailure.InvalidResponse, failure.Failure);
    }
    [Fact] public void OverlayWireHasBoundedUtf8AndNeverSerializesExecutableObjects()
    {
        var bytes = GuideOverlayWire.Encode(GuideOverlayDocument.From("g", "合成作品", Document()), ["合成来源"]);
        using var binary = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        Assert.Equal("合成作品", Encoding.UTF8.GetString(binary.ReadBytes(binary.ReadInt32())));
        Assert.Throws<InvalidDataException>(() => GuideOverlayWire.Encode(GuideOverlayDocument.Message("g", "198", new string('x', 32769), "")));
    }
    [Theory]
    [InlineData("action", "7月18日", true)]
    [InlineData("action", "７月１８日", true)]
    [InlineData("action", "2000年7月18日（金）", true)]
    [InlineData("choice", "7月18日", false)]
    [InlineData("save", "7月18日", false)]
    [InlineData("action", "7月18日以后选择第一项", false)]
    public void DateHeadingFormattingIsConservativeAndNeverChangesChoiceMeaning(string kind, string value, bool heading)
    {
        var step = new WalkthroughStep(kind, new(value, value, false), null, []);
        Assert.Equal(heading, GuideReadingText.IsHeading(step)); Assert.Equal(value, step.Text.Original);
    }
    [Theory]
    [InlineData("■４月１４日（月）━━━━━━━━━━", "■４月１４日（一）━━━━━━━━━━")]
    [InlineData("■寧々AFTER━━━━━", "■宁宁AFTER━━━━━")]
    [InlineData("第一章", "第一章")]
    public void ExplicitRemoteHeadingsRemainHeadingsInReadingAndOverlay(string original, string display)
    {
        string heading = JsonSerializer.Serialize(new { step_type = "heading", text = original,
            localized = new Dictionary<string, object> { ["zh-Hans"] = new { value = display, is_machine = true } },
            alternatives = Array.Empty<object>(), remark = (string?)null, save_key = (string?)null });
        using var json = JsonDocument.Parse(Synthetic.Replace("{\"step_type\":\"action\",\"text\":\"Continue\",\"alternatives\":[]}", heading));
        var document = NextMoeWalkthroughs.Parse(json.RootElement, "198");
        var step = Assert.Single(document.Segments[1].Steps);
        Assert.Equal("heading", step.Kind); Assert.Equal(original, step.Text.Original); Assert.Equal(display, step.Text.Display);
        Assert.True(GuideReadingText.IsHeading(step));
        Assert.Empty(document.Segments[1].Steps.Where(s => !GuideReadingText.IsHeading(s)));
        var overlay = GuideOverlayDocument.From("g", "合成作品", document);
        Assert.True(overlay.Routes[1].Steps[0].Heading);
        Assert.Equal(original, overlay.Routes[1].Steps[0].Original);
        Assert.Equal(display, overlay.Routes[1].Steps[0].Display);
        Assert.NotEmpty(GuideOverlayWire.Encode(overlay));
        Assert.Equal(1, WalkthroughFlowGraph.Build(document).Edges.Sum(edge => edge.Choices));
    }
    [Theory]
    [InlineData("script")]
    [InlineData("choice_v99")]
    public void UnknownRemoteStepKindsAreStillRejectedRatherThanDropped(string kind)
    {
        using var json = JsonDocument.Parse(Synthetic.Replace("\"step_type\":\"action\"", "\"step_type\":" + JsonSerializer.Serialize(kind)));
        var failure = Assert.Throws<OnlineException>(() => NextMoeWalkthroughs.Parse(json.RootElement, "198"));
        Assert.Equal(OnlineFailure.InvalidResponse, failure.Failure);
    }
    [Fact] public void ReadingWireSeparatesSaveAlternativesAndDateLayoutWithoutFlatteningSourceText()
    {
        var guide = Document(); var first = guide.Segments[0];
        var date = new WalkthroughStep("action", new("７月１８日", "7月18日", true), null, []);
        guide = guide with { Segments = guide.Segments.SetItem(0, first with { Steps = first.Steps.Insert(0, date) }) };
        var document = GuideOverlayDocument.From("g", "合成", guide);
        Assert.Equal(3, document.Routes[0].Steps.Length); Assert.True(document.Routes[0].Steps[0].Heading);
        Assert.Equal("手紙を読む", document.Routes[0].Steps[2].Original);
        Assert.Equal("读信", document.Routes[0].Steps[2].Display);
        using var reader = new BinaryReader(new MemoryStream(GuideOverlayWire.Encode(document)), Encoding.UTF8);
        string Text() => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
        Text(); Text(); Text(); Assert.Equal(0, reader.ReadInt32()); Assert.Equal(2, reader.ReadInt32());
        Text(); Text(); Text(); Text(); Assert.Equal(3, reader.ReadInt32());
        Assert.Equal("action", Text()); Assert.Equal("７月１８日", Text()); Assert.Equal("7月18日", Text());
        Assert.Equal(1, reader.ReadInt32()); Assert.Equal("", Text()); Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(1, reader.ReadInt32()); Assert.Equal(0, reader.ReadInt32());
        Assert.Equal("save", Text()); Text(); Text(); reader.ReadInt32(); Assert.Equal("save1", Text());
        reader.ReadInt32(); reader.ReadInt32(); Assert.Equal(0, reader.ReadInt32());
        Assert.Equal("choice", Text()); Assert.Equal("手紙を読む", Text()); Assert.Equal("读信", Text());
        reader.ReadInt32(); Text(); reader.ReadInt32(); reader.ReadInt32(); Assert.Equal(1, reader.ReadInt32());
        Assert.Equal("待つ", Text()); Assert.Equal("等待", Text()); Assert.Equal(0, reader.ReadInt32());
    }
    [Fact] public void CollectionTerminalHasNoReferencedKeyAndIsNotAnInvalidDanglingEnding()
    {
        using var json = JsonDocument.Parse(Synthetic.Replace("\"node_type\":\"ending\",\"key\":\"end1\"", "\"node_type\":\"collect\",\"key\":null"));
        var guide = NextMoeWalkthroughs.Parse(json.RootElement, "198");
        Assert.All(guide.Segments, segment => { Assert.Equal("collect", segment.To.Kind); Assert.Equal("回收后结束", segment.To.Label.Display); });
    }
    [Fact] public void RouteUnlockRequirementsArePreservedAndUnknownPrerequisitesAreRejected()
    {
        using var json = JsonDocument.Parse(Synthetic.Replace("\"label\":\"START\"", "\"label\":\"START\",\"unlocked_by_ending_key\":\"end1\""));
        var guide = NextMoeWalkthroughs.Parse(json.RootElement, "198");
        Assert.Equal("A END", guide.Segments[0].From.Requirement);
        Assert.Contains("需先完成", GuideOverlayDocument.From("g", "合成", guide).Routes[0].From);
        using var invalid = JsonDocument.Parse(Synthetic.Replace("\"label\":\"START\"", "\"label\":\"START\",\"unlocked_by_ending_key\":\"missing\""));
        Assert.Throws<OnlineException>(() => NextMoeWalkthroughs.Parse(invalid.RootElement, "198"));
    }
    [Theory]
    [InlineData("\"ending_count\":1", "\"ending_count\":\"one\"")]
    [InlineData("{\"key\":\"common\",\"title\":\"共通\"}", "42")]
    public void MalformedRemoteTypesProduceAHandledFailure(string before, string after)
    {
        using var json = JsonDocument.Parse(Synthetic.Replace(before, after));
        Assert.Throws<OnlineException>(() => NextMoeWalkthroughs.Parse(json.RootElement, "198"));
    }
    private sealed class Credential : ICredentialProvider
    { public Task<string?> ReadAsync(CancellationToken cancellation) => Task.FromResult<string?>("synthetic-key"); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => Task.FromResult(reply(request)); }
    private static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    [Fact] public async Task ListUsesExactWorkIdentityContentPreferenceAndOpaqueCursor()
    {
        Uri? actual = null;
        using var http = new HttpClient(new Handler(request => { actual = request.RequestUri; return Json("{\"items\":[],\"next_cursor\":null}"); }));
        var reader = new NextMoeWalkthroughs(new(http, new Credential(), new("https://api.nextmoe.dev/v2/")));
        var page = await reader.ListAsync("198", "cur_test", true, default);
        Assert.Empty(page.Items); Assert.Equal("/v2/catalog/walkthroughs", actual!.AbsolutePath);
        Assert.Contains("work_id=198", actual.Query); Assert.Contains("cursor=cur_test", actual.Query); Assert.Contains("nsfw=true", actual.Query);
    }
    [Fact] public async Task FilteredOrMissingDetailRemainsNotFoundRatherThanAnEmptySuccessfulGuide()
    {
        using var http = new HttpClient(new Handler(_ => Json("{\"code\":\"NOT_FOUND\"}", HttpStatusCode.NotFound)));
        var reader = new NextMoeWalkthroughs(new(http, new Credential(), new("https://api.nextmoe.dev/v2/")));
        var failure = await Assert.ThrowsAsync<OnlineException>(() => reader.ReadAsync("17", "198", false, default));
        Assert.Equal(OnlineFailure.NotFound, failure.Failure);
    }
    private sealed class Reader : IWalkthroughReader
    {
        public Func<string, string?, bool, CancellationToken, Task<WalkthroughPage>> List = (_, _, _, _) => Task.FromResult(new WalkthroughPage([Document().Summary], null));
        public Func<string, string, bool, CancellationToken, Task<WalkthroughDocument>> Read = (id, work, _, _) => Task.FromResult(Document(id, work));
        public Task<WalkthroughPage> ListAsync(string work, string? cursor, bool nsfw, CancellationToken cancellation) => List(work, cursor, nsfw, cancellation);
        public Task<WalkthroughDocument> ReadAsync(string id, string work, bool nsfw, CancellationToken cancellation) => Read(id, work, nsfw, cancellation);
    }
    [Fact] public async Task LateListFromPreviousWorkCannotPopulateCurrentReader()
    {
        var late = new TaskCompletionSource<WalkthroughPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Reader { List = (_, _, _, _) => late.Task };
        using var model = new WalkthroughViewModel(reader); model.SetSource("198"); var pending = model.RefreshAsync();
        model.SetSource("384"); late.SetResult(new([Document().Summary], null)); await pending;
        Assert.Equal("384", model.WorkId); Assert.Empty(model.Items); Assert.Null(model.Document); Assert.False(model.Busy);
    }
    [Fact] public async Task CancelledLateDetailCannotOverwriteNewSelection()
    {
        var late = new TaskCompletionSource<WalkthroughDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Reader { List = (_, _, _, _) => Task.FromResult(new WalkthroughPage([Document().Summary, Document("18").Summary], null)),
            Read = (id, _, _, _) => id == "17" ? late.Task : Task.FromResult(Document("18")) };
        using var model = new WalkthroughViewModel(reader); model.SetSource("198"); await model.RefreshAsync();
        var old = model.SelectAsync("17"); await model.SelectAsync("18"); late.SetResult(Document()); await old;
        Assert.Equal("18", model.Document!.Summary.Id); Assert.False(model.Busy);
    }
    [Fact] public async Task LoginChangeDuringRequestRejectsOldContent()
    {
        var late = new TaskCompletionSource<WalkthroughPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Reader { List = (_, _, _, _) => late.Task }; long auth = 0;
        using var model = new WalkthroughViewModel(reader, () => auth); model.SetSource("198"); var pending = model.RefreshAsync();
        auth++; late.SetResult(new([Document().Summary], null)); await pending;
        Assert.Empty(model.Items); Assert.Null(model.Document);
    }
    [Fact] public async Task FailureAndTrueEmptyHaveDifferentMessages()
    {
        var reader = new Reader { List = (_, _, _, _) => Task.FromResult(new WalkthroughPage([], null)) };
        using var model = new WalkthroughViewModel(reader); model.SetSource("198"); await model.RefreshAsync();
        Assert.Contains("暂无", model.Message);
        reader.List = (_, _, _, _) => throw new OnlineException(OnlineFailure.Offline); await model.RefreshAsync();
        Assert.Contains("失败", model.Message); Assert.DoesNotContain("暂无", model.Message);
    }
    [Fact] public async Task KnownWorkUsesDirectDetailScopeForBothListAndSteps()
    {
        var reader = new Reader {
            List = (_, _, nsfw, _) => Task.FromResult(new WalkthroughPage(nsfw ? [Document().Summary] : [], null)),
            Read = (id, work, nsfw, _) => { Assert.True(nsfw); return Task.FromResult(Document(id, work)); }
        };
        using var model = new WalkthroughViewModel(reader); model.SetSource("198");
        await model.RefreshAsync(); Assert.Single(model.Items);
        await model.SelectAsync(model.Items[0].Id); Assert.NotNull(model.Document);
    }
}
