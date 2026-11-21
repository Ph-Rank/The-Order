using System.Text;

namespace TheOrder.Application;

[Flags]
public enum GameChatStyle { Bold = 1, Italic = 2, Code = 4, Link = 8, Strike = 16, Heading = 32 }
public sealed record GameChatSpan(int Offset, int Length, GameChatStyle Style);

// Separate from the guide document: streaming a reply must not copy a guide or
// its page bitmap on every token. Offsets are shared with GuideChat.hpp.
public static class GameChatWire
{
    public const int Magic = 0x54414348, Version = 2, Header = 256, Input = 256, InputBytes = 16384,
        Draft = Input + InputBytes, State = Draft + InputBytes, StateBytes = 1048576,
        Pixels = State + StateBytes, PixelBytes = 8388608, MaximumBytes = Pixels + PixelBytes;
    public const int MaxModels = 512, MaxMessageCharacters = 40000, MaxSpans = 2048;
    public static byte[] Encode(GameChatState state)
    {
        if (state.Models.Length > MaxModels) throw new InvalidDataException("Game chat model list exceeds its limit");
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer, Encoding.UTF8, true);
        void Text(string value, int max)
        {
            value = value.Replace("\0", "");
            if (value.Length > max) value = value[..(char.IsHighSurrogate(value[max - 1]) ? max - 1 : max)];
            Bytes(Encoding.UTF8.GetBytes(value));
        }
        void Bytes(byte[] bytes) { writer.Write(bytes.Length); writer.Write(bytes); }
        Text(state.Status, 300); Text(state.Model, 160); Text(state.Effort, 16);
        writer.Write(state.Busy ? 1 : 0); writer.Write(state.ImageInput ? 1 : 0); writer.Write(state.Search ? 1 : 0); writer.Write(state.Spoiler ? 1 : 0);
        writer.Write(state.Models.Length);
        foreach (var item in state.Models) Text(item, 180);
        writer.Write(state.Messages.Length);
        foreach (var item in state.Messages)
        {
            bool truncated = item.Text.Length > MaxMessageCharacters;
            string body = item.Text;
            if (truncated)
            {
                int cutoff = MaxMessageCharacters - 40;
                if (char.IsHighSurrogate(body[cutoff - 1])) cutoff--;
                body = body[..cutoff] + "\n\n（长回复可在主程序继续阅读）";
            }
            var byteText = Encoding.UTF8.GetBytes(body.Replace("\0", ""));
            Text(item.Role, 16); Bytes(byteText); Text(item.Meta, 300);
            var spans = truncated || body.Contains('\0') ? [] : ValidSpans(item.Styles, byteText);
            writer.Write(spans.Count);
            foreach (var span in spans)
            {
                writer.Write(span.Offset);
                writer.Write(span.Length);
                writer.Write((int)span.Style);
            }
        }
        writer.Flush();
        if (buffer.Length > StateBytes && state.Messages.Length > 1) return Encode(state with { Messages = state.Messages[1..] });
        if (buffer.Length > StateBytes) throw new InvalidDataException("Game chat projection too large");
        return buffer.ToArray();
    }

    private static List<GameChatSpan> ValidSpans(GameChatSpan[]? spans, byte[] text)
    {
        var result = new List<GameChatSpan>();
        if (spans is null) return result;
        int previous = 0;
        foreach (var span in spans.Take(MaxSpans))
        {
            if (span.Offset < previous || span.Offset >= text.Length || span.Length <= 0 || span.Length > text.Length - span.Offset
                || (int)span.Style is <= 0 or > 63) continue;
            int end = span.Offset + span.Length;
            if (!Boundary(span.Offset) || !Boundary(end)) continue;
            result.Add(span);
            previous = end;
        }
        return result;

        bool Boundary(int index) => index == text.Length || (text[index] & 0xc0) != 0x80;
    }
}
public sealed record GameChatLine(string Role, string Text, string Meta = "", GameChatSpan[]? Styles = null);
public sealed record GameChatState(string Status, string Model, string Effort, bool Busy, bool ImageInput,
    bool Search, bool Spoiler, string[] Models, GameChatLine[] Messages);
