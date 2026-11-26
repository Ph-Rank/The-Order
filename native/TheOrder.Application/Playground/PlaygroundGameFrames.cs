using System.Collections.Immutable;
using System.Text.RegularExpressions;
using TheOrder.Domain;

namespace TheOrder.Application.Playground;

// Automatic frames are transient, unlike explicitly attached documents. Apply
// this projection to saves, exports and old-turn context, not to the live turn.
public static class PlaygroundGameFrames
{
    public static PlaygroundAttachment WithoutPixels(PlaygroundAttachment a) => !a.IsGameFrame ? a :
        new(a.Name, "当轮游戏画面已释放，未保存图像；不能从此记录重新读取画面。") { IsGameFrame = true };
    public static ImmutableArray<PlaygroundAttachment> WithoutPixels(ImmutableArray<PlaygroundAttachment> values)
        => values.Select(WithoutPixels).ToImmutableArray();
}

public sealed partial class PlaygroundService
{
    internal static string GameOverlayKeyFor(LibraryEntry entry) =>
        $"game:{entry.Id}:{Hash(WorkKeyFor(entry))["sha256:".Length..]}";

    private string? ResolveGameTitle(Run run)
    {
        if (run.Conversation.Options.GameOverlayKey is not { } key || !key.StartsWith("game:", StringComparison.Ordinal))
            return null;
        var matches = ReadLibrary(run)?.Entries
            .Where(entry => entry.Metadata is not null && key.StartsWith($"game:{entry.Id}:", StringComparison.Ordinal)
                && key == GameOverlayKeyFor(entry)).Take(2).ToArray();
        if (matches is not { Length: 1 } || matches[0].Metadata is not { } metadata)
            return null;
        return PublicCatalogTitle(metadata);
    }

    private const string GameQuoteTool = "search_game_quote";
    private bool GameQuoteAvailable(Run run) => SearchAvailable(run)
        && run.Attachments.Any(a => a.IsGameFrame && IsImageAttachment(a))
        && Provider(run.Conversation.Options.Provider).Capabilities(run.Conversation.Options.Model, run.Conversation.Options.Endpoint).Tools;
    private static readonly Regex PrivateGameQuote = new(
        @"密码|密钥|凭据|账号|账户|邮箱|手机号|身份证|银行卡|住址|api[\s_-]?key|bearer|authorization|sk-[A-Za-z0-9_-]|AQ\.|https?://|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|(?:[A-Za-z]:|\\\\)[/\\]|\b\+?\d[\d -]{9,}\d\b|PRIVATE|CANARY",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal static bool SafeGameQuote(string quote) => quote.Length is >= 3 and <= 160
        && !quote.Any(char.IsControl) && !PrivateGameQuote.IsMatch(quote);
    private async Task<string> SearchGameQuote(Run run, string quote)
    {
        if (!GameQuoteAvailable(run)) return Result("permission_denied");
        quote = quote.Trim();
        if (!SafeGameQuote(quote)) return Result("invalid_arguments", notice: "只能核实当前游戏画面中的短句，不能提交账户、凭据、路径或联系方式。");
        if (run.GameQuotePayloads.TryGetValue(quote, out var cached)) return cached;
        var previous = (run.SafePublicQuestion, run.PublicSearchAttempted, run.PublicSearchPayload);
        run.GameQuotePayloads[quote] = Result("not_available", notice: "本句已尝试核实，未自动重复付费请求。");
        try
        {
            run.SafePublicQuestion = "查证这句公开作品台词的原文出处及一手来源。检索范围是下面一句：" + quote;
            run.PublicSearchAttempted = false; run.PublicSearchPayload = null;
            return run.GameQuotePayloads[quote] = await PublicSearch(run).ConfigureAwait(false);
        }
        finally { (run.SafePublicQuestion, run.PublicSearchAttempted, run.PublicSearchPayload) = previous; }
    }
}
