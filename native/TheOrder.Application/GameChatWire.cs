using System.Text;

namespace TheOrder.Application;

// Separate from the guide document: streaming a reply must not copy a guide or
// its page bitmap on every token. Offsets are shared with GuideChat.hpp.
public static class GameChatWire
{
    public const int Magic = 0x54414348, Version = 1, Header = 256, Input = 256, InputBytes = 16384,
        Draft = Input + InputBytes, State = Draft + InputBytes, StateBytes = 1048576,
        Pixels = State + StateBytes, PixelBytes = 8388608, MaximumBytes = Pixels + PixelBytes;
    public const int MaxModels = 512;
    public static byte[] Encode(GameChatState state)
    {
        if (state.Models.Length > MaxModels) throw new InvalidDataException("Game chat model list exceeds its limit");
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer, Encoding.UTF8, true);
        void Text(string value, int max) { value = value.Replace("\0", ""); if (value.Length > max) value = value[..max]; var b = Encoding.UTF8.GetBytes(value); writer.Write(b.Length); writer.Write(b); }
        Text(state.Status, 300); Text(state.Model, 160); Text(state.Effort, 16);
        writer.Write(state.Busy ? 1 : 0); writer.Write(state.ImageInput ? 1 : 0); writer.Write(state.Search ? 1 : 0); writer.Write(state.Spoiler ? 1 : 0);
        writer.Write(state.Models.Length);
        foreach (var item in state.Models) Text(item, 180);
        writer.Write(state.Messages.Length);
        foreach (var item in state.Messages)
        {
            string body = item.Text.Length > 40000 ? item.Text[..39960] + "\n\n（长回复可在主程序继续阅读）" : item.Text;
            Text(item.Role, 16); Text(body, 40000); Text(item.Meta, 300);
        }
        writer.Flush();
        if (buffer.Length > StateBytes && state.Messages.Length > 1) return Encode(state with { Messages = state.Messages[1..] });
        if (buffer.Length > StateBytes) throw new InvalidDataException("Game chat projection too large");
        return buffer.ToArray();
    }
}
public sealed record GameChatLine(string Role, string Text, string Meta = "");
public sealed record GameChatState(string Status, string Model, string Effort, bool Busy, bool ImageInput,
    bool Search, bool Spoiler, string[] Models, GameChatLine[] Messages);
