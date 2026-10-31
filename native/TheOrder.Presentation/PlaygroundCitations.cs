using System.Globalization;
using System.Text.RegularExpressions;
using TheOrder.Application.Playground;

namespace TheOrder.Presentation;

public sealed record PlaygroundCitationPart(string Text, PlaygroundSource? Source = null, int? Number = null);
public sealed record PlaygroundCitationEntry(PlaygroundSource Source, int? Number);

// Owned by one displayed message, including its committed streaming blocks.
// Numbers are allocated by the renderer, so code examples and unused context
// sources never consume a footnote number. Stored messages stay untouched.
public sealed class PlaygroundCitations
{
    private static readonly Regex Token = new(@"\[\[([A-Za-z0-9_-]{1,160})\]\]", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, PlaygroundSource> known = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> numbers = new(StringComparer.Ordinal);
    private readonly List<string> order = [];
    public PlaygroundCitations(IEnumerable<PlaygroundSource> sources)
    {
        foreach (var source in sources.Take(64)) known.TryAdd(source.Id, source);
    }
    public bool AllowsUrl(string url) => known.Values.Any(s => s.Kind == PlaygroundSourceKind.Web && s.Url == url);
    public IEnumerable<PlaygroundCitationEntry> Entries => order.Select(id => new PlaygroundCitationEntry(known[id], numbers[id]))
        .Concat(known.Values.Where(s => !numbers.ContainsKey(s.Id)).Select(s => new PlaygroundCitationEntry(s, null)));
    public IReadOnlyList<PlaygroundCitationPart> Literal(string value)
    {
        var parts = new List<PlaygroundCitationPart>(); var group = new List<PlaygroundCitationPart>(); int at = 0;
        void FlushGroup()
        {
            bool first = true;
            foreach (var citation in group.DistinctBy(p => p.Number).OrderBy(p => p.Number))
            {
                if (!first) parts.Add(new("\u2060\u2009\u2060"));
                parts.Add(citation); first = false;
            }
            group.Clear();
        }
        foreach (Match match in Token.Matches(value))
        {
            bool verified = known.TryGetValue(match.Groups[1].Value, out var source);
            string preceding = value[at..match.Index];
            bool continuesGroup = verified && group.Count > 0 && IsGroupSeparator(preceding);
            if (!continuesGroup)
            {
                FlushGroup();
                if (verified) preceding = preceding.TrimEnd(' ', '\u00a0');
                if (preceding.Length > 0) parts.Add(new(preceding));
            }
            if (verified)
            {
                if (!numbers.TryGetValue(source!.Id, out int number)) { number = order.Count + 1; order.Add(source.Id); numbers.Add(source.Id, number); }
                group.Add(new(Superscript(number), source, number));
            }
            else parts.Add(new("[未核验引用]"));
            at = match.Index + match.Length;
        }
        FlushGroup();
        if (at < value.Length) parts.Add(new(value[at..]));
        return parts;
    }
    private static bool IsGroupSeparator(string text) => text.All(c => c is ' ' or '\t' or '\u00a0' or ',' or '，' or '、' or ';' or '；');
    private static string Superscript(int number) => "\u2060" + string.Join("\u2060", number.ToString(CultureInfo.InvariantCulture).Select(c => "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));
}
