using System.Collections.Immutable;

namespace TheOrder.Domain;

public static class GameProgress
{
    // Keep the persisted pre-existing "planned" value for 未设置; 想玩 is a distinct Native value.
    public const string Default = "planned";
    public static readonly ImmutableArray<string> Ordered = ["completed", "playing", "wish", "paused", "dropped", "planned"];
    public static bool Valid(string value) => Ordered.Contains(value);
    public static string Higher(string first, string second)
    {
        var a = Ordered.IndexOf(first); var b = Ordered.IndexOf(second);
        if (a < 0 || b < 0) throw new InvalidDataException("游玩状态无效。");
        return a <= b ? first : second;
    }
    public static string Label(string value) => value switch
    { "completed" => "玩过", "playing" => "在玩", "wish" => "想玩", "paused" => "搁置", "dropped" => "放弃", "planned" => "未设置", _ => throw new InvalidDataException("游玩状态无效。") };
}
