using System.Collections.Immutable;

namespace TheOrder.Application;

public sealed record WalkthroughSummary(string Id, string WorkId, string Source, string? SourceUrl,
    string Language, string Release, int Endings, int Saves, int Segments, string UpdatedAt);
public sealed record WalkthroughPage(ImmutableArray<WalkthroughSummary> Items, string? NextCursor);
public sealed record WalkthroughText(string Original, string Display, bool Machine);
public sealed record WalkthroughStep(string Kind, WalkthroughText Text, string? SaveKey,
    ImmutableArray<WalkthroughText> Alternatives, bool Achievement = false);
public sealed record WalkthroughNode(string Kind, string Key, WalkthroughText Label, string? Requirement = null)
{
    public string? RequirementKey { get; init; }
    public string? EndingType { get; init; }
}
public sealed record WalkthroughSection(string Key, WalkthroughText Title);
public sealed record WalkthroughSegment(string Key, string Section, WalkthroughNode From,
    WalkthroughNode To, WalkthroughText Condition, ImmutableArray<WalkthroughStep> Steps);
public sealed record WalkthroughDocument(WalkthroughSummary Summary, bool GameHasHints,
    ImmutableArray<WalkthroughSection> Sections, ImmutableArray<WalkthroughNode> Nodes,
    ImmutableArray<WalkthroughSegment> Segments);
public interface IWalkthroughReader
{
    Task<WalkthroughPage> ListAsync(string workId, string? cursor, bool showNsfw, CancellationToken cancellation);
    Task<WalkthroughDocument> ReadAsync(string id, string workId, bool showNsfw, CancellationToken cancellation);
}

// This is a transient reading projection, not library metadata or a downloaded game file.
public sealed record GuideOverlayStep(string Kind, string Original, string Display, bool Machine,
    string SaveKey = "", bool Achievement = false, bool Heading = false, ImmutableArray<WalkthroughText> Alternatives = default);
public sealed record GuideOverlayRoute(string Key, string Title, string From, string To,
    ImmutableArray<GuideOverlayStep> Steps);
public sealed record GuideOverlayDocument(string GameId, string WorkId, string Title, string Attribution,
    string Status, ImmutableArray<GuideOverlayRoute> Routes)
{
    public GuideFlowLayout? Flow { get; init; }
    public GuideLocalReading? Reading { get; init; }
    public static GuideOverlayDocument Message(string gameId, string workId, string title, string message) =>
        new(gameId, workId, title, "", message, []);
    public static GuideOverlayDocument From(string gameId, string title, WalkthroughDocument document)
    {
        var sections = document.Sections.ToDictionary(s => s.Key, s => s.Title.Display, StringComparer.Ordinal);
        return new(gameId, document.Summary.WorkId, title,
            $"来源：{GuideReadingText.SourceName(document.Summary.Source)} · {document.Summary.Release}", "",
            document.Segments.Select(segment => new GuideOverlayRoute(segment.Key,
                (sections.GetValueOrDefault(segment.Section) ?? "路线") +
                (string.IsNullOrWhiteSpace(segment.Condition.Display) ? "" : " · " + segment.Condition.Display),
                segment.From.Label.Display + (segment.From.Requirement is null ? "" : "（需先完成：" + segment.From.Requirement + "）"), segment.To.Label.Display,
                segment.Steps.Select(step => new GuideOverlayStep(step.Kind, step.Text.Original, step.Text.Display,
                    step.Text.Machine, step.SaveKey ?? "", step.Achievement, GuideReadingText.IsHeading(step), step.Alternatives)).ToImmutableArray())).ToImmutableArray());
    }
}

// Geometry is calculated by the manager, never by a browser/interpreter inside
// the game. Only bounded plain records cross the shared-memory boundary.
public sealed record GuideFlowPoint(float X,float Y);
public sealed record GuideFlowNode(string Kind,string Label,float X,float Y,float Width,float Height,string EndingType="");
public sealed record GuideFlowEdge(int From,int To,int Route,int Start,int Count,int Choices,int Section,bool Unlock,ImmutableArray<GuideFlowPoint> Points,GuideFlowPoint? Label=null);
public sealed record GuideFlowLayout(float Width,float Height,ImmutableArray<GuideFlowNode> Nodes,ImmutableArray<GuideFlowEdge> Edges,bool Truncated);
public interface IGuideFlowLayout { Task<GuideFlowLayout?> CreateAsync(WalkthroughDocument document,CancellationToken cancellation); }

// Presentation only: never infer a route, change a choice, or translate source text.
public static class GuideReadingText
{
    private static readonly System.Text.RegularExpressions.Regex Date = new(
        @"^(?:[0-9０-９]{2,4}年)?[0-9０-９]{1,2}月[0-9０-９]{1,2}日(?:\s*[（(][月火水木金土日一二三四五六天曜曜日\s]+[）)])?$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.NonBacktracking);
    public static bool IsHeading(WalkthroughStep step) => step.Kind == "heading" || step.Kind == "action" &&
        (Date.IsMatch(step.Text.Display.Trim()) || Date.IsMatch(step.Text.Original.Trim()));
    public static string SourceName(string source) => source switch
    { "seiya" => "誠也の部屋", "foolmaker" => "愚者之馆", "native" => "鲲社区", _ => source };
}
