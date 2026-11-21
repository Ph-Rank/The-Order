using System.Text;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TheOrder.Application;

namespace TheOrder.Presentation;

public static class GameChatMarkdown
{
    public static GameChatLine Format(GameChatLine line)
    {
        if (line.Role != "assistant" || string.IsNullOrWhiteSpace(line.Text)) return line;
        var input = line.Text.Replace("\0", "");
        bool shortened = input.Length > GameChatWire.MaxMessageCharacters - 50;
        if (shortened)
        {
            int cutoff = GameChatWire.MaxMessageCharacters - 50;
            if (char.IsHighSurrogate(input[cutoff - 1])) cutoff--;
            input = input[..cutoff];
        }
        try
        {
            var rendered = new Projection();
            rendered.Blocks(MarkdownContent.Parse(input), 0);
            if (shortened || rendered.Limited)
                rendered.Append("\n\n（长回复可在主程序继续阅读）");
            return rendered.Text.Length == 0 ? line : line with { Text = rendered.Text.ToString(), Styles = rendered.Styles.ToArray() };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { return line; }
    }

    private sealed class Projection
    {
        internal StringBuilder Text { get; } = new();
        internal List<GameChatSpan> Styles { get; } = [];
        private int bytes, nodes;
        internal bool Limited => nodes > 4096 || Text.Length >= GameChatWire.MaxMessageCharacters - 30;

        internal void Append(string value, GameChatStyle style = 0)
        {
            int count = Math.Min(value.Length, GameChatWire.MaxMessageCharacters - Text.Length);
            if (count == 0) return;
            if (count < value.Length && char.IsHighSurrogate(value[count - 1])) count--;
            if (count == 0) return;
            int length = Encoding.UTF8.GetByteCount(value.AsSpan(0, count));
            Text.Append(value.AsSpan(0, count));
            if (style != 0)
            {
                if (Styles.Count > 0 && Styles[^1] is { } last && last.Style == style && last.Offset + last.Length == bytes)
                    Styles[^1] = last with { Length = last.Length + length };
                else if (Styles.Count < GameChatWire.MaxSpans) Styles.Add(new(bytes, length, style));
            }
            bytes += length;
        }

        internal void Blocks(ContainerBlock container, int depth, GameChatStyle style = 0)
        {
            bool first = true;
            foreach (var block in container)
            {
                if (Limited) break;
                if (!first) Append("\n\n");
                first = false;
                Block(block, depth, style);
            }
        }

        private void Block(Block block, int depth, GameChatStyle style)
        {
            if (++nodes > 4096 || depth > 16) return;
            switch (block)
            {
                case HeadingBlock heading:
                    Inlines(heading.Inline, style | GameChatStyle.Heading | GameChatStyle.Bold, 0);
                    break;
                case CodeBlock code:
                    Append(code.Lines.ToString().Replace("\t", "    "), style | GameChatStyle.Code);
                    break;
                case ListBlock list:
                    int index = int.TryParse(list.OrderedStart, out int start) ? start : 1;
                    bool first = true;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        if (Limited) break;
                        if (!first) Append("\n");
                        first = false;
                        if (depth > 0) Append(new string(' ', depth * 2));
                        Append(list.IsOrdered ? (index++) + ". " : "• ");
                        bool firstBlock = true;
                        foreach (var child in item)
                        {
                            if (!firstBlock) Append("\n");
                            firstBlock = false;
                            Block(child, child is ListBlock ? depth + 1 : depth, style);
                        }
                    }
                    break;
                case QuoteBlock quote:
                    bool firstQuote = true;
                    foreach (var child in quote)
                    {
                        if (Limited) break;
                        if (!firstQuote) Append("\n");
                        firstQuote = false;
                        Append("│ ");
                        Block(child, depth + 1, style | GameChatStyle.Italic);
                    }
                    break;
                case Table table:
                    bool firstRow = true;
                    foreach (var row in table.OfType<TableRow>())
                    {
                        if (Limited) break;
                        if (!firstRow) Append("\n");
                        firstRow = false;
                        bool firstCell = true;
                        foreach (var cell in row.OfType<TableCell>())
                        {
                            if (Limited) break;
                            if (!firstCell) Append("  │  ");
                            firstCell = false;
                            Blocks(cell, depth + 1, row.IsHeader ? style | GameChatStyle.Bold : style);
                        }
                    }
                    break;
                case ThematicBreakBlock:
                    Append("────────");
                    break;
                case LeafBlock leaf:
                    Inlines(leaf.Inline, style, 0);
                    break;
                case ContainerBlock nested:
                    Blocks(nested, depth + 1, style);
                    break;
            }
        }

        private void Inlines(ContainerInline? source, GameChatStyle style, int depth)
        {
            if (depth > 24) return;
            for (var item = source?.FirstChild; item is not null && !Limited; item = item.NextSibling)
            {
                ++nodes;
                switch (item)
                {
                    case LiteralInline literal: Append(literal.Content.ToString(), style); break;
                    case HtmlEntityInline entity: Append(entity.Transcoded.ToString(), style); break;
                    case LineBreakInline: Append("\n"); break;
                    case CodeInline code: Append(code.Content, style | GameChatStyle.Code); break;
                    case TaskList task: Append(task.Checked ? "☑ " : "☐ ", style); break;
                    case EmphasisInline emphasis:
                        var flag = emphasis.DelimiterChar == '~' ? GameChatStyle.Strike
                            : emphasis.DelimiterCount >= 2 ? GameChatStyle.Bold : GameChatStyle.Italic;
                        Inlines(emphasis, style | flag, depth + 1);
                        break;
                    case LinkInline link:
                        var url = link.IsImage ? null : MarkdownContent.ResolveLink(link.Url);
                        int begin = Text.Length;
                        Inlines(link, url is null ? style : style | GameChatStyle.Link, depth + 1);
                        if (url is not null && !Text.ToString(begin, Text.Length - begin).Equals(url, StringComparison.Ordinal))
                            Append(begin == Text.Length ? url : " (" + url + ")", style | GameChatStyle.Link);
                        break;
                    case AutolinkInline auto: Append(auto.Url, MarkdownContent.AllowedLink(auto.Url) ? style | GameChatStyle.Link : style); break;
                    case ContainerInline nested: Inlines(nested, style, depth + 1); break;
                    default: Append(item.ToString() ?? "", style); break;
                }
            }
        }
    }
}
