using System.Text.RegularExpressions;

namespace TheOrder.Application.Playground;

public sealed partial class PlaygroundService
{
    private sealed record SafePreference(string Category, string Stance, string Value, string Text);
    private static SafePreference? SafePreferenceFor(PlaygroundMemory memory)
    {
        // Recognize complete, short preference sentences against closed
        // vocabularies. Extracting a harmless-looking clause from a story is
        // insufficient: neither the rest of the quote nor an inferred summary
        // may slip into a non-spoiler prompt through its evidence field.
        if (memory.Kind == "inferred" || memory.Text.Length > 120) return null;
        var text = memory.Text.Trim().TrimEnd('。', '.', '！', '!').Trim();
        const string music = "钢琴(?:曲|音乐)?|管弦乐|电子(?:乐|音乐)|摇滚(?:乐)?|爵士(?:乐)?|抒情(?:歌|音乐)?|轻音乐|纯音乐|原声音乐|背景音乐|游戏音乐|古典音乐|音乐";
        const string pace = "慢节奏(?:的作品)?|快节奏(?:的作品)?|节奏缓慢(?:的作品)?|节奏紧凑(?:的作品)?|缓慢的节奏|紧凑的节奏";
        const string genre = "视觉小说|恋爱(?:题材|作品)?|悬疑(?:题材|作品)?|科幻(?:题材|作品)?|奇幻(?:题材|作品)?|日常(?:题材|作品)?|喜剧|推理(?:题材|作品)?|冒险(?:题材|作品)?|治愈(?:题材|作品)?";
        var chinese = Regex.Match(text, "^我(?:个人)?(?<stance>更喜欢|更偏好|不喜欢|不偏好|喜欢|喜爱|偏好|讨厌)(?<value>" + music + "|" + pace + "|" + genre + ")$", RegexOptions.CultureInvariant);
        if (chinese.Success)
        {
            string value = chinese.Groups["value"].Value;
            string category = Regex.IsMatch(value, "^(?:" + music + ")$") ? "音乐" : Regex.IsMatch(value, "^(?:" + pace + ")$") ? "节奏" : "类型";
            string stance = chinese.Groups["stance"].Value.StartsWith('不') || chinese.Groups["stance"].Value == "讨厌" ? "不偏好" : "偏好";
            return new(category, stance, value, category + "：" + stance + value);
        }
        var english = Regex.Match(text, @"^I\s+(?<stance>like|love|prefer|dislike)\s+(?<value>piano music|orchestral music|electronic music|jazz|rock music|classical music|slow pacing|fast pacing|visual novels|science fiction|fantasy|mystery|comedy)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (english.Success)
        {
            string value = english.Groups["value"].Value.ToLowerInvariant();
            string category = value.Contains("music") || value == "jazz" ? "音乐" : value.Contains("pacing") ? "节奏" : "类型";
            string stance = english.Groups["stance"].Value.Equals("dislike", StringComparison.OrdinalIgnoreCase) ? "不偏好" : "偏好";
            return new(category, stance, value, category + "：" + stance + value);
        }
        return null;
    }
}
