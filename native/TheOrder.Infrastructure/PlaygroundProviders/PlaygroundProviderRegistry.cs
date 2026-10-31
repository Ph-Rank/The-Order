using System.Collections.Immutable;
using TheOrder.Application.Playground;

namespace TheOrder.Infrastructure.PlaygroundProviders;

// Official services only. API credentials never form part of provider requests,
// model metadata, continuations, source records or diagnostics.
public sealed class PlaygroundProviderRegistry
{
    public ImmutableArray<IPlaygroundProvider> Providers { get; }
    public PlaygroundProviderRegistry(HttpClient client, IPlaygroundCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(client); ArgumentNullException.ThrowIfNull(credentials);
        Providers = Enum.GetValues<PlaygroundProviderKind>().Select(kind =>
            (IPlaygroundProvider)new NativePlaygroundProvider(kind, client, credentials)).ToImmutableArray();
    }
    public static string NormalizeEndpoint(PlaygroundProviderKind provider, string endpoint) => PlaygroundProviderEndpoints.Normalize(provider, endpoint);
    public static string OfficialEndpoint(PlaygroundProviderKind provider) => PlaygroundProviderEndpoints.Default(provider);
}

public static class PlaygroundProviderEndpoints
{
    public static string Default(PlaygroundProviderKind provider) => PlaygroundEndpointPolicy.Default(provider);
    public static string Normalize(PlaygroundProviderKind provider, string endpoint)
    {
        try { return PlaygroundEndpointPolicy.Normalize(provider, string.IsNullOrWhiteSpace(endpoint) ? Default(provider) : endpoint.Trim()); }
        catch (ArgumentException) { throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "仅支持此服务商的官方 HTTPS 端点与路径。")); }
    }
}

internal sealed partial class NativePlaygroundProvider(PlaygroundProviderKind kind, HttpClient client, IPlaygroundCredentials credentials) : IPlaygroundProvider
{
    public PlaygroundProviderKind Kind { get; } = kind;
    public string DefaultEndpoint => PlaygroundProviderEndpoints.Default(Kind);
    private const string ClaudeVersion = "2026-10-21";
    public PlaygroundCapabilities Capabilities(string model, string endpoint)
    {
        PlaygroundProviderEndpoints.Normalize(Kind, endpoint);
        model = string.IsNullOrWhiteSpace(model) ? "" : CleanModel(model);
        bool verified = Kind switch
        {
            PlaygroundProviderKind.OpenAI => model is "gpt-6-astra" or "gpt-6.1-sol" or "gpt-6.1-terra" or "gpt-6-luna" or "gpt-5.4" or "gpt-5.5",
            PlaygroundProviderKind.Gemini => model is "gemini-4-argon" or "gemini-3.8-flash" or "gemini-3.6-flash",
            PlaygroundProviderKind.Claude => model is "claude-opus-5-5" or "claude-opus-5" or "claude-opus-4-6" or "claude-sonnet-4-6",
            PlaygroundProviderKind.Grok => model is "grok-4.7",
            PlaygroundProviderKind.DeepSeek => model is "deepseek-flash" or "deepseek-v4-pro",
            _ => false
        };
        return new(verified, Streaming: true, Tools: verified, Search: verified)
        {
            ImageInput = verified && (Kind != PlaygroundProviderKind.DeepSeek || model == "deepseek-flash"),
            // DeepSeek medium is a documented compatibility alias for high,
            // not an additional native tier; the notice exposes that limit.
            ReasoningEfforts = Kind == PlaygroundProviderKind.OpenAI ? model switch
            {
                "gpt-6-astra" or "gpt-6.1-sol" => ["low", "medium", "high", "xhigh", "max"],
                "gpt-6.1-terra" or "gpt-6-luna" => ["none", "low", "medium", "high", "xhigh", "max"],
                "gpt-5.4" or "gpt-5.5" => ["none", "low", "medium", "high", "xhigh"],
                _ => []
            } : Kind == PlaygroundProviderKind.DeepSeek && verified ? ["none", "low", "medium", "high", "max"]
                : Kind == PlaygroundProviderKind.Grok && verified ? ["low", "medium", "high", "xhigh"]
                : Kind == PlaygroundProviderKind.Gemini && verified ? ["low", "medium", "high"]
                : Kind == PlaygroundProviderKind.Claude && verified ? model is "claude-opus-5-5" or "claude-opus-5"
                    ? ["low", "medium", "high", "xhigh", "max"] : ["low", "medium", "high", "max"] : [],
            Notice = (Kind == PlaygroundProviderKind.Claude ? "请使用限定工作区的 API Key；未限定 Key 暂不支持 workspace header。" : "")
                + (!verified ? "此模型未核实高级能力，仅尝试基础文字对话；可手动输入官方有效模型 ID。"
                : Kind == PlaygroundProviderKind.Gemini ? "使用 Interactions；Google 搜索答复仅本次会话展示。"
                : Kind == PlaygroundProviderKind.Claude ? "支持自适应思考与 effort；默认 high。"
                : Kind == PlaygroundProviderKind.Grok ? "默认思考级别为 high；可降低思考级别，不能关闭。"
                : Kind == PlaygroundProviderKind.DeepSeek ? "联网使用 DeepSeek 自研搜索与网页读取；中档为官方兼容值，实际与高档相同。" : null)
        };
    }
    private static string CleanModel(string model)
    {
        model = model.Trim();
        if (model.StartsWith("models/", StringComparison.Ordinal)) model = model[7..];
        if (model.Length is < 1 or > 160 || model.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '/')))
            throw new PlaygroundProviderException(new(PlaygroundFailureKind.ModelUnavailable, "请填写有效的模型 ID。"));
        return model;
    }
    private static PlaygroundProviderException Invalid(string message = "服务商返回的数据不完整或格式不受支持。") => new(new(PlaygroundFailureKind.InvalidResponse, message));
}
