namespace TheOrder.Infrastructure.Recognition;

// Removes trailing release-format noise from untrusted local titles: distribution channel,
// package tier, fan-translation build tags and build versions. These name a package of the same
// work, never a different work. Edition markers (remake, FD, anniversary...) are kept; they are
// resolved against catalog releases by ReleaseAwareRanking. Pure and bounded.
public static class LocalTitleCleaner
{
    private const string Format = @"(?:ダウンロード版|ＤＬ版|DL版|パッケージ版|通常版|初回(?:限定|生産)?版|(?:完全生産|数量|期間)?限定版|豪華(?:限定)?版|廉価版|新装版|普及版|特装版|プレミアム版|"
        + @"抱き枕カバー(?:付き?)?(?:版)?|(?:(?:download|digital|steam|physical|regular|standard|limited|censored|uncensored|collector'?s|us|jp|eu|asia|english|japanese|chinese)\s+)+edition|"
        + @"(?:简体|繁体|簡體|繁體|官方)?中文(?:硬盘)?版|官中版?|汉化(?:硬盘|整合|完整)?版|免安装(?:版)?|硬盘版|绿色版|直装版|解压即玩|(?:含)?全CG存档|(?:无|無)(?:修正?|码)版?|步兵版)";
    private static readonly string[] Trailing = [
        @"\s*[-_~～〜\s]*[（(【\[]?\s*" + Format + @"\s*[）)】\]]?\s*$",
        @"(?:\s*[-_]\s*|\s+)(?:CD|DVD|BD)\s*[- ]?GAME\s*$",
        @"\s*[（(【\[][^（）()【】\[\]]{0,30}(?:汉化|漢化|翻译|翻譯|字幕)(?:组|組|社|版)?[^（）()【】\[\]]{0,10}[）)】\]]\s*$",
        @"[\s_]+v(?:er\.?)?\s*\d+(?:\.\d+)+[a-z]?\s*$",
    ];

    // File names additionally carry build bitness ("dracuriot64", "Season264"), which would
    // otherwise read as a different chapter number.
    public static string Clean(string value, string source)
    {
        var title = Clean(value);
        if (source is "executable-name" or "archive-name") return Bitness(title);
        if (source is "folder" or "product-name" or "file-description" or "manual-title" or "config-title")
        {
            // The bundled VNDB v17909 row names this edition in full. Expand the known local
            // shorthand before lookup, and let the normal title, index and release checks decide.
            var key = TitleMatching.IndexKey(title);
            if (key is "tsukihimer" or "月姬r" or "月姫r" ||
                source == "file-description" && key is ("月姬r本地pc应用" or "月姫r本地pc應用"))
                return TitleMatching.TsukihimeRemakeTitle;
        }
        return title;
    }
    private static string Bitness(string value)
    {
        var result = TitleMatching.Rx(@"(?:[\s._-]*(?:x64|x86|win64|win32|amd64)|(?<=[\p{L}\d])[._-]?(?:64|32))$", true).Replace(value, "").Trim();
        return TitleMatching.IndexKey(result).Length >= 3 ? result : value;
    }

    public static string Clean(string value)
    {
        if (value.Length > 500) return value;
        string result = value.Trim(), previous;
        int rounds = 0;
        do
        {
            previous = result;
            foreach (var pattern in Trailing) result = TitleMatching.Rx(pattern, true).Replace(result, "").Trim();
        } while (result != previous && ++rounds < 6);
        // Never reduce a title to nothing or to an uninformative fragment.
        return TitleMatching.IndexKey(result).Length >= 2 ? result : value.Trim();
    }
}
