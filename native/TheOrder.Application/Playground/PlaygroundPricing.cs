using System.Globalization;

namespace TheOrder.Application.Playground;

public enum PlaygroundChargeKind { Discussion, PublicSearch, PublicFetch }
public sealed record PlaygroundCostEstimate(string Currency, decimal? Minimum, decimal? Maximum,
    bool IsActual, bool IsComplete, string Notice);
public sealed record PlaygroundPriceQuote(string Currency, decimal InputPerMillion, decimal OutputPerMillion,
    decimal CachedInputPerMillion, string SourceUrl, DateTimeOffset VerifiedAt, string Notice);

// Public list prices, not account balances, negotiated discounts or invoices.
// Exact IDs only: an unknown model never inherits another generation's price.
public static class PlaygroundPricing
{
    private const string OpenAiSource = "https://developers.openai.com/api/docs/pricing";
    private const string GoogleSource = "https://ai.google.dev/gemini-api/docs/pricing";
    private const string ClaudeSource = "https://platform.claude.com/docs/en/about-claude/pricing";
    private const string GrokSource = "https://docs.x.ai/developers/pricing";
    private const string DeepSeekSource = "https://api-docs.deepseek.com/quick_start/pricing/";
    private static readonly DateTimeOffset Verified = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
    private sealed record Rate(decimal Input, decimal Output, decimal Cache, string Source,
        long? LongThreshold = null, decimal LongInput = 1, decimal LongOutput = 1,
        string Notice = "");
    private static Rate? Rates(PlaygroundProviderKind provider, string model, DateTimeOffset at)
    {
        if (provider == PlaygroundProviderKind.OpenAI)
        {
            var rates = model switch
            {
                "gpt-6-astra" => (10m, 50m, 1m), "gpt-6.1-sol" => (2m, 10m, .1m),
                "gpt-6.1-terra" => (1m, 5m, .05m), "gpt-6-luna" => (.1m, .5m, .01m),
                "gpt-5.4" => (2.5m, 15m, .25m), "gpt-5.5" => (5m, 30m, .5m),
                _ => (0m, 0m, 0m)
            };
            return rates.Item1 == 0 ? null : new(rates.Item1, rates.Item2, rates.Item3, OpenAiSource,
                272000, 2, 1.5m, "标准档；单次输入超过272K时整次输入/缓存×2、输出×1.5。");
        }
        if (provider == PlaygroundProviderKind.Gemini)
        {
            bool argonPromo = at < new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
            bool flashPromo = at < new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return model switch
            {
                "gemini-4-argon" => argonPromo ? new(2, 10, .1m, GoogleSource, Notice: "推广价至2026-11-30；12月起4/20/0.2美元。")
                    : new(4, 20, .2m, GoogleSource),
                "gemini-3.8-flash" or "gemini-3.6-flash" => flashPromo
                    ? new(.75m, 3.75m, .075m, GoogleSource, Notice: "推广价至2026-12-31；其后1.5/7.5/0.15美元。")
                    : new(1.5m, 7.5m, .15m, GoogleSource),
                "gemini-3.5-flash-lite" => new(.3m, 2.5m, .03m, GoogleSource),
                "gemini-3.1-pro-preview" => new(2, 12, .2m, GoogleSource, 200000, 2, 1.5m,
                    "单次输入超过200K时整次输入/缓存×2、输出×1.5。"),
                _ => null
            };
        }
        if (provider == PlaygroundProviderKind.Claude) return model switch
        {
            "claude-fable-5-1" => new(10, 50, .25m, ClaudeSource),
            "claude-fable-5" => new(10, 50, 1, ClaudeSource),
            "claude-opus-5-5" => new(4, 20, .2m, ClaudeSource),
            "claude-opus-5" or "claude-opus-4-6" => new(5, 25, .5m, ClaudeSource),
            "claude-sonnet-5-5" => new(2, 10, .1m, ClaudeSource),
            "claude-sonnet-5" => new(2, 10, .2m, ClaudeSource),
            "claude-sonnet-4-6" => new(3, 15, .3m, ClaudeSource),
            "claude-haiku-5-5" => new(.1m, .5m, .01m, ClaudeSource, 100000, 5, 5,
                "单次全部输入超过100K时整次各类token价格×5。"),
            _ => null
        };
        if (provider == PlaygroundProviderKind.Grok && model == "grok-4.7")
            return new(2, 6, .5m, GrokSource, 199999, 2, 2, "单次全部输入达到200K时整次各类token价格×2。");
        if (provider == PlaygroundProviderKind.DeepSeek) return model switch
        {
            "deepseek-flash" or "deepseek-v4-flash" or "deepseek-v4-flash-vision-exp" => new(.3m, 1.2m, .006m, DeepSeekSource,
                Notice: "谷时半价；峰窗UTC工作日01–04/06–10且排除中国法定节假日。"),
            "deepseek-v4-pro" => new(1.32m, 3.96m, .044m, DeepSeekSource,
                Notice: "谷时半价；峰窗UTC工作日01–04/06–10且排除中国法定节假日。"),
            _ => null
        };
        return null;
    }
    public static PlaygroundPriceQuote? Quote(PlaygroundProviderKind provider, string model, DateTimeOffset at)
    {
        var rate = Rates(provider, model, at);
        return rate is null ? null : new("USD", rate.Input, rate.Output, rate.Cache, rate.Source, Verified,
            rate.Notice + " 每百万token；输出价包含思考。缓存命中另计，搜索另计；不代表账户折扣或最终账单。");
    }
    public static string Describe(PlaygroundProviderKind provider, string model, DateTimeOffset at)
    {
        var rate = Rates(provider, model, at);
        if (rate is null) return "收费标准尚未核实";
        string Range(decimal value) => provider == PlaygroundProviderKind.DeepSeek ? Number(value / 2) + "–" + Number(value) : Number(value);
        return "输入 US$" + Range(rate.Input) + " · 输出 US$" + Range(rate.Output) + " / 百万 token"
            + (provider == PlaygroundProviderKind.DeepSeek ? "（峰谷）" : rate.LongThreshold is not null ? "（长上下文另价）" : "");
    }
    public static PlaygroundCostEstimate Calculate(PlaygroundProviderKind provider, string model, PlaygroundUsage? usage,
        DateTimeOffset at, PlaygroundChargeKind kind = PlaygroundChargeKind.Discussion)
    {
        if (usage is null || !IsValidUsage(usage)) return Unknown("未取得完整计费用量；请求可能已经计费。");
        if (provider == PlaygroundProviderKind.Grok && usage.ReportedCostUsd is { } actual) return new("USD", actual, actual, true, true,
            "服务商返回的本次实际收费，已含其工具与折扣；不是账户余额。");
        if (provider == PlaygroundProviderKind.DeepSeek && kind != PlaygroundChargeKind.Discussion)
        {
            int? count = kind == PlaygroundChargeKind.PublicSearch ? usage.Searches : usage.Fetches;
            if (count is null) return Unknown("未取得独立搜索/网页读取成功请求数。");
            decimal fee = at < new DateTimeOffset(2026, 10, 31, 16, 0, 0, TimeSpan.Zero) ? 0 : count.Value * .00075m;
            return new("USD", fee, fee, false, true, "按DeepSeek成功请求数与调用时段估算；北京时间2026-11-01前搜索/Fetch免费，其后每千次US$0.75。");
        }
        string billModel = usage.BillingModel ?? model; var rate = Rates(provider, billModel, at);
        if (rate is null) return Unknown("该实际计费型号的单价尚未核实，未套用其他型号价格。");
        decimal low = 0, high = 0; bool complete = true; var notices = new List<string>();
        bool longContext = usage.InputTokens > rate.LongThreshold && rate.LongThreshold is not null;
        decimal inputRate = rate.Input * (longContext ? rate.LongInput : 1), cacheRate = rate.Cache * (longContext ? rate.LongInput : 1);
        decimal outputRate = rate.Output * (longContext ? rate.LongOutput : 1);
        if (longContext) notices.Add("已按本次全部输入的长上下文价格计算。");
        if (usage.InputTokens is { } input)
        {
            long write = usage.CacheWriteInputTokens ?? 0; long cached = usage.CachedInputTokens ?? 0;
            long ordinary = input - write - cached;
            low += ordinary * inputRate / 1000000m; high = low;
            low += cached * cacheRate / 1000000m; high += cached * cacheRate / 1000000m;
            if (usage.CachedInputTokens is null)
            { low -= ordinary * (inputRate - cacheRate) / 1000000m; notices.Add("缓存命中未返回，保留缓存至普通输入价格区间。"); }
            if (write > 0)
            {
                long shorter = provider == PlaygroundProviderKind.OpenAI ? usage.CacheWrite30mInputTokens ?? 0 : usage.CacheWrite5mInputTokens ?? 0;
                long longer = provider == PlaygroundProviderKind.OpenAI ? usage.CacheWrite12hInputTokens ?? 0 : usage.CacheWrite1hInputTokens ?? 0;
                long unknown = write - shorter - longer; decimal longMultiplier = provider == PlaygroundProviderKind.OpenAI ? 2.5m : 2;
                low += (shorter * 1.25m + longer * longMultiplier + unknown * 1.25m) * inputRate / 1000000m;
                high += (shorter * 1.25m + longer * longMultiplier + unknown * longMultiplier) * inputRate / 1000000m;
                if (unknown > 0) notices.Add("缓存写入保留时长未知，按已核实两档区间估算。");
            }
        }
        else { complete = false; notices.Add("缺少输入用量。"); }
        if (usage.OutputTokens is { } output)
        {
            if (usage.ReasoningTokens > output) return Unknown("旧记录的输出/思考口径无法确认，未重复计费或猜测。");
            low += output * outputRate / 1000000m; high += output * outputRate / 1000000m;
        }
        else { complete = false; notices.Add("缺少输出用量。"); }
        if (usage.InputTokens is null && rate.LongThreshold is not null)
        { high *= Math.Max(rate.LongInput, rate.LongOutput); notices.Add("输入规模未知，不能确认长上下文档。"); }
        if (provider == PlaygroundProviderKind.DeepSeek)
        {
            var utc = at.UtcDateTime;
            bool peakWindow = utc.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                && (utc.Hour is >= 1 and < 4 or >= 6 and < 10);
            low /= 2; if (!peakWindow) high /= 2;
            if (peakWindow) notices.Add("中国法定节假日未核实，按峰/谷区间估算。");
        }
        if (!ApplyTier(provider, usage.ServiceTier, ref low, ref high))
        { complete = false; notices.Add("服务档位价格尚未核实。"); }
        if (kind == PlaygroundChargeKind.PublicSearch)
        {
            if (usage.Searches is not { } searches) { complete = false; notices.Add("搜索次数未返回，未含未知搜索费。"); }
            else if (provider == PlaygroundProviderKind.Gemini)
            { high += searches * .014m; if (searches > 0) notices.Add("Google共享免费额度未知，搜索费按US$0–0.014/查询计入区间。"); }
            else { decimal searchPrice = provider == PlaygroundProviderKind.Grok ? .005m : .01m; low += searches * searchPrice; high += searches * searchPrice; }
        }
        if (usage.InputTokens is null && usage.OutputTokens is null
            && (kind != PlaygroundChargeKind.PublicSearch || usage.Searches is not > 0)) return Unknown(string.Join(" ", notices));
        return new("USD", low, high, false, complete, "按公开单价估算（非账单）；每次请求单独计价，思考已含输出。 "
            + string.Join(" ", notices) + " 来源核验2026-10-31：" + rate.Source);
    }
    private static bool ApplyTier(PlaygroundProviderKind provider, string? tier, ref decimal low, ref decimal high)
    {
        if (tier is null or "default" or "standard" or "auto") return true;
        decimal? multiplier = provider switch
        {
            PlaygroundProviderKind.OpenAI => tier switch { "fast" or "priority" => 2m, "ultrafast" => 6m, "flex" or "batch" => .5m, _ => null },
            PlaygroundProviderKind.Grok => tier switch { "priority" => 2m, "batch" => .8m, _ => null },
            _ => null
        };
        if (multiplier is null) return false; low *= multiplier.Value; high *= multiplier.Value; return true;
    }
    public static PlaygroundCostEstimate Combine(PlaygroundCostEstimate? previous, PlaygroundCostEstimate next)
    {
        if (previous is null) return next;
        if (!IsValidCost(previous) || !IsValidCost(next) || previous.Currency != next.Currency) return Unknown("费用无法可靠合并。");
        decimal? min = previous.Minimum is null && next.Minimum is null ? null : (previous.Minimum ?? 0) + (next.Minimum ?? 0);
        decimal? max = previous.Maximum is null && next.Maximum is null ? null : (previous.Maximum ?? 0) + (next.Maximum ?? 0);
        bool complete = previous.IsComplete && next.IsComplete;
        string notice = previous.Notice == next.Notice ? previous.Notice : previous.Notice + " " + next.Notice;
        return new("USD", min, max, previous.IsActual && next.IsActual && complete, complete,
            notice.Length <= 1800 ? notice : notice[..1800]);
    }
    public static PlaygroundCostEstimate ForMessage(PlaygroundMessage message)
    {
        if (message.Cost is { } saved && IsValidCost(saved) && (!saved.IsActual
            || message.Provider == PlaygroundProviderKind.Grok && message.Usage?.ReportedCostUsd == saved.Minimum)) return saved;
        if (message.Provider is not { } provider || message.Model is not { Length: > 0 } model) return Unknown("旧记录缺少计费型号。");
        if (IsLegacyClaudeUsage(message))
        {
            if (!IsValidStoredUsage(message)) return Unknown("旧Claude用量无效，未推定为免费请求。");
            var usage = message.Usage!;
            // Only old Claude stored uncached input separately from cache hits.
            // Interpret a copy for display; never migrate or rewrite the record.
            var known = usage with { InputTokens = usage.InputTokens is { } input
                ? input + (usage.CachedInputTokens ?? 0) : null, CachedInputTokens = usage.CachedInputTokens ?? 0 };
            var legacy = Calculate(provider, model, known, message.CreatedAt);
            return legacy with { IsActual = false, IsComplete = false,
                Notice = "旧Claude记录：输入为未缓存用量，缓存命中另列；仅估算可确认部分，缓存写入及逐请求档位未知。 " + legacy.Notice };
        }
        var estimate = Calculate(provider, model, message.Usage, message.CreatedAt);
        // Historical Usage aggregates tool rounds: tier thresholds and models
        // cannot be reconstructed from that sum. Never call it a complete bill.
        return estimate with { IsActual = false, IsComplete = false,
            Notice = "旧记录按消息时间与汇总用量估算；缺少逐请求档位、研究型号和搜索计费信息。 " + estimate.Notice };
    }
    public static string Format(PlaygroundCostEstimate? cost)
    {
        if (cost is null || !IsValidCost(cost) || cost.Minimum is null || cost.Maximum is null) return "费用未知";
        string amount = cost.Minimum == cost.Maximum ? Money(cost.Minimum.Value) : Money(cost.Minimum.Value) + "–" + Money(cost.Maximum.Value);
        return (cost.IsActual ? "实际 " : cost.IsComplete ? "估算 " : "部分估算 ") + amount;
    }
    private static string Money(decimal value) => "US$" + (value > 0 && value < .000001m ? "<0.000001" : value.ToString("0.######", CultureInfo.InvariantCulture));
    private static string Number(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    public static PlaygroundCostEstimate Unknown(string notice) => new("USD", null, null, false, false, notice);
    public static bool IsLegacyClaudeUsage(PlaygroundMessage message) => message.Provider == PlaygroundProviderKind.Claude
        && message.Role == "assistant" && message.Cost is null && message.Usage is { } usage
        && usage.BillingModel is null && usage.ServiceTier is null && usage.ReportedCostUsd is null
        && usage.CacheWriteInputTokens is null && usage.CacheWrite5mInputTokens is null && usage.CacheWrite1hInputTokens is null
        && usage.CacheWrite30mInputTokens is null && usage.CacheWrite12hInputTokens is null;
    public static bool IsValidStoredUsage(PlaygroundMessage message)
    {
        if (!IsLegacyClaudeUsage(message)) return IsValidUsage(message.Usage);
        var usage = message.Usage!;
        // CachedInputTokens was an independent old bucket, so it may exceed
        // InputTokens. All bounds and the remaining relationships stay strict.
        return usage.CachedInputTokens is not < 0 and not > 1000000000000
            && IsValidUsage(usage with { CachedInputTokens = null });
    }
    public static bool IsValidCost(PlaygroundCostEstimate? cost) => cost is null || cost.Currency == "USD"
        && cost.Minimum is not < 0 and not > 1000000000m && cost.Maximum is not < 0 and not > 1000000000m
        && (cost.Minimum is null) == (cost.Maximum is null) && (cost.Minimum is null || cost.Maximum >= cost.Minimum)
        && (!cost.IsActual || cost.IsComplete && cost.Minimum is not null && cost.Minimum == cost.Maximum)
        && (!cost.IsComplete || cost.Minimum is not null) && cost.Notice is { Length: <= 2000 };
    public static bool IsValidUsage(PlaygroundUsage? usage)
    {
        if (usage is null) return true;
        long?[] counts = [usage.InputTokens, usage.OutputTokens, usage.ReasoningTokens, usage.CachedInputTokens, usage.CacheWriteInputTokens,
            usage.CacheWrite5mInputTokens, usage.CacheWrite1hInputTokens, usage.CacheWrite30mInputTokens, usage.CacheWrite12hInputTokens];
        if (counts.Any(count => count is < 0 or > 1000000000000) || usage.Searches is < 0 || usage.Fetches is < 0
            || usage.ReportedCostUsd is < 0 or > 1000000000m || usage.BillingModel?.Length > 180 || usage.BillingModel?.Any(char.IsControl) == true
            || usage.BillingModel is not null && string.IsNullOrWhiteSpace(usage.BillingModel)
            || usage.ServiceTier?.Length > 64 || usage.ServiceTier?.Any(char.IsControl) == true
            || usage.ServiceTier is not null && string.IsNullOrWhiteSpace(usage.ServiceTier)
            || usage.ReasoningTokens is { } reasoning && usage.OutputTokens is { } output && reasoning > output) return false;
        long writes = usage.CacheWriteInputTokens ?? 0;
        return (usage.InputTokens is null || writes + (usage.CachedInputTokens ?? 0) <= usage.InputTokens)
            && (usage.CacheWrite5mInputTokens ?? 0) + (usage.CacheWrite1hInputTokens ?? 0) <= writes
            && (usage.CacheWrite30mInputTokens ?? 0) + (usage.CacheWrite12hInputTokens ?? 0) <= writes;
    }
}
