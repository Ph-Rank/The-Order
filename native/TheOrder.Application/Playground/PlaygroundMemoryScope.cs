using System.Text.RegularExpressions;

namespace TheOrder.Application.Playground;

public sealed partial class PlaygroundService
{
    private static bool SensitiveMemorySubject(string text)
    {
        if (PrivateOpinion(text)) return true;
        // Check the whole user statement, not only an entertainment clause a
        // model chose to quote. Ordinary chat is retained; this gate controls
        // only creation of durable preferences.
        if (Regex.IsMatch(text, @"(?<!\d)(?:\+?86[-\s]?)?1[3-9](?:[-\s]?\d){9}(?!\d)|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)) return true;
        string[] sensitive = ["手机号", "电话号码", "联系方式", "微信号", "邮箱", "身份证", "银行卡", "住在", "我住", "住址", "居住", "家庭地址", "街道", "门牌", "邮编", "我家", "我的家",
            "我的工作", "我的职业", "工作地点", "工资", "收入", "我就读", "就读于", "我的学校", "我的生日", "我出生", "婚姻", "已婚", "未婚", "我的孩子", "我的家人",
            "宗教", "信仰", "信奉", "信徒", "佛教", "基督", "天主", "伊斯兰", "穆斯林", "犹太教", "印度教", "神道", "道教", "信佛", "圣经", "古兰经", "无神论",
            "民族", "种族", "国籍", "性取向", "政治立场", "党派", "病史", "病历", "患病", "生病", "确诊", "诊断", "治疗", "住院", "手术", "服药", "用药", "怀孕", "流产",
            "抑郁", "焦虑症", "糖尿病", "癌症", "自闭症", "多动症", "ADHD", "HIV", "高血压", "癫痫", "艾滋", "乙肝", "过敏", "二甲双胍", "胰岛素",
            "散步", "睡觉", "做饭", "烹饪", "旅游", "旅行", "健身", "饮食", "宠物", "我喜欢吃", "我喜欢运动", "我喜欢养", "我的身高", "我的体重"];
        if (sensitive.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))) return true;
        return Regex.IsMatch(text, @"(?:[路街巷弄区县镇村].{0,12}\d+\s*号)|(?:我(?:今年)?\s*\d{1,3}\s*岁)|\b(?:my\s+(?:address|phone|email|family|job|salary|birthday)|i\s+live|years\s+old|christian|catholic|buddhist|muslim|islam|hindu|judaism|jewish|sikh|taoist|scientology|diagnosed|diabetes|depression|autism|adhd|hiv|allergic|medication)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
    private bool MemorySubjectAllowed(Run run, string text)
    {
        if (SensitiveMemorySubject(run.Question) || SensitiveMemorySubject(text)) return false;
        if (Regex.IsMatch(text, @"ACGN|galgame|视觉小说|游戏|动画|动漫|漫画|小说|番剧|剧情|叙事|故事|剧本|文笔|人物|角色|人设|立绘|画风|美术|配乐|音乐|音效|声优|演出|玩法|战斗|谜题|解谜|关卡|节奏|结局|路线|分支|科幻|悬疑|奇幻|恋爱题材|治愈题材|音楽|物語|シナリオ|キャラクター|作画|ゲーム|アニメ|小説|声優|\b(?:anime|manga|visual\s+novels?|games?|gaming|music|soundtracks?|characters?|art\s*style|gameplay|pacing|plots?|narrative|fiction)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        if (Regex.IsMatch(text, "[《「『][^》」』\\r\\n]{2,100}[》」』]", RegexOptions.CultureInvariant)) return true;
        if (!Can(run, "library")) return false;
        return ReadLibrary(run)?.Entries.Any(e => e.Metadata is { } metadata && new[] { metadata.Title, metadata.OriginalTitle }
            .Concat(metadata.Aliases).Where(t => !string.IsNullOrWhiteSpace(t)).Any(t => AliasIn(text, t))) == true;
    }
}
