using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;

namespace TheOrder.WinUI.Views;

// Presentation labels only. Provider values, identities and the cached evidence remain unchanged.
internal static class MusicDetailFormatting
{
    internal static string Role(string value) => value switch
    {
        "composer" => "作曲", "lyricist" => "作词", "arranger" => "编曲", "producer" => "制作",
        "engineer" => "音频工程", "recording" => "录音", "mix" => "混音", "mastering" => "母带",
        "vocal" => "演唱", "instrument" => "演奏", "conductor" => "指挥", "orchestrator" => "配器",
        "design/illustration" => "设计 / 插画", "design" => "设计", "illustration" => "插画",
        "photography" => "摄影", "art direction" => "美术指导", "programming" => "音乐编程",
        "executive producer" => "执行制作", "chorus master" => "合唱指挥", "instrument arranger" => "乐器编配", _ => value
    };
    internal static string Attribute(string value) => value switch
    {
        "strings" => "弦乐", "acoustic guitar" => "原声吉他", "electric guitar" => "电吉他",
        "bass" => "贝斯", "bass guitar" => "贝斯吉他", "drums (drum set)" => "架子鼓",
        "piano" => "钢琴", "keyboard" or "keyboards" => "键盘", "violin" => "小提琴", "cello" => "大提琴",
        "guitar" => "吉他", "percussion" => "打击乐", "membranophone" => "膜鸣乐器",
        "lead vocals" => "主唱", "background vocals" => "和声", "additional" => "补充", "guest" => "客席",
        "solo" => "独奏 / 独唱", _ => value
    };
    internal static (string Role, string Attributes) SplitCredit(MusicBrainzCredit credit)
    {
        var parts = credit.Role.Split(" · ", 2, StringSplitOptions.None);
        return (parts[0], parts.Length == 1 ? "" : string.Join(" / ", parts[1].Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(Attribute)));
    }
    internal static string Credit(MusicBrainzCredit credit)
    {
        var parts = SplitCredit(credit);
        return Role(parts.Role) + (parts.Attributes.Length > 0 ? "（" + parts.Attributes + "）" : "") + " · " + credit.Name;
    }
    internal static string Language(string value) => value switch { "jpn" => "日语", "eng" => "英语", "zho" => "中文", "deu" => "德语", "fra" => "法语", "kor" => "韩语", "und" => "未注明", "zxx" => "无语言内容", _ => value };
    internal static string WorkType(string? value) => value switch { "Song" => "歌曲", "Instrumental" => "器乐作品", "Soundtrack" => "配乐作品", "Opera" => "歌剧", "Symphony" => "交响曲", _ => value ?? "" };
    internal static string EntityType(string? value) => value switch { "Person" => "个人", "Group" => "团体", "Orchestra" => "管弦乐团", "Choir" => "合唱团", "Character" => "角色", _ => WorkType(value) };
    internal static string Packaging(string value) => value switch { "Jewel Case" => "标准 CD 盒", "Slim Jewel Case" => "薄型 CD 盒", "Digipak" => "折叠纸盒", "Cardboard/Paper Sleeve" => "纸套", "Keep Case" => "塑料盒", "Other" => "其他包装", _ => value };

    internal static FrameworkElement Fact(string label, string value)
    {
        var name = Text(label, 12); name.Opacity = .72;
        return new StackPanel { Spacing = 3, MinWidth = 100, Children = { name, Text(value, 14) } };
    }
    internal static TagFlowPanel Facts() => new() { HorizontalSpacing = 26, VerticalSpacing = 12, MaximumItemWidth = 380, CenterInlineItems = false };
    internal static void AddFact(TagFlowPanel panel, string label, string? value)
    { if (!string.IsNullOrWhiteSpace(value)) panel.Children.Add(Fact(label, value)); }

    internal static StackPanel Credits(IEnumerable<MusicBrainzCredit> values, Func<MusicBrainzCredit, UIElement> entry)
    {
        var body = new StackPanel { Spacing = 10 };
        foreach (var group in values.GroupBy(x => SplitCredit(x).Role))
        {
            var row = new Grid { ColumnSpacing = 14 };
            row.ColumnDefinitions.Add(new() { Width = new(78) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var label = Text(Role(group.Key), 13); label.Opacity = .75; label.Margin = new(0, 7, 0, 0);
            ToolTipService.SetToolTip(label, group.Key); row.Children.Add(label);
            var people = new TagFlowPanel { HorizontalSpacing = 14, VerticalSpacing = 4, MaximumItemWidth = 310, CenterInlineItems = false };
            foreach (var credit in group) people.Children.Add(entry(credit));
            Grid.SetColumn(people, 1); row.Children.Add(people); body.Children.Add(row);
        }
        return body;
    }
    internal static string CreditPerson(MusicBrainzCredit credit)
    {
        var parts = SplitCredit(credit);
        return credit.Name + (parts.Attributes.Length == 0 ? "" : " · " + parts.Attributes);
    }
    internal static TagFlowPanel Artists(IEnumerable<MusicBrainzArtistCredit> credits, Func<MusicBrainzReference, Task>? navigate)
    {
        var row = new TagFlowPanel { HorizontalSpacing = 0, VerticalSpacing = 4, MaximumItemWidth = 310 };
        foreach (var credit in credits)
        {
            if (credit.ArtistId is { } id)
            {
                var reference = new MusicBrainzReference(MusicBrainzEntityKind.Artist, id);
                var label = Text(credit.Name, 14); label.IsHitTestVisible = false;
                var link = navigate is null
                    ? new ExternalLinkButton()
                    : new HyperlinkButton { Padding = new(0, 3, 0, 3), MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Left };
                link.Content = label;
                AutomationProperties.SetAutomationId(link, "MusicArtist-" + id);
                AutomationProperties.SetName(link, credit.Name + " · 音乐人 / 团体");
                ToolTipService.SetToolTip(link, reference.Url);
                link.Click += async (_, _) =>
                {
                    try
                    {
                        if (navigate is not null) { await navigate(reference); return; }
                        if (!MusicBrainzReference.TryParse(reference.Url, out _) || !await Windows.System.Launcher.LaunchUriAsync(new Uri(reference.Url)))
                            throw new InvalidOperationException();
                    }
                    catch { label.Text = credit.Name + "（未能打开，可重试）"; }
                };
                row.Children.Add(link);
            }
            else row.Children.Add(Text(credit.Name, 14));
            if (credit.JoinPhrase.Length > 0) row.Children.Add(Text(credit.JoinPhrase, 14));
        }
        return row;
    }
    internal static TextBlock Text(string value, double size) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
}
