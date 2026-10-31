namespace TheOrder.Application.Playground;

public static class PlaygroundEndpointPolicy
{
    public static string Default(PlaygroundProviderKind provider) => provider switch
    {
        PlaygroundProviderKind.OpenAI => "https://api.openai.com/v1",
        PlaygroundProviderKind.Gemini => "https://generativelanguage.googleapis.com/v1beta",
        PlaygroundProviderKind.Claude => "https://api.anthropic.com/v1",
        PlaygroundProviderKind.Grok => "https://api.x.ai/v1",
        PlaygroundProviderKind.DeepSeek => "https://api.deepseek.com",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
    public static string Normalize(PlaygroundProviderKind provider, string endpoint)
    {
        var expected = new Uri(Default(provider));
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !uri.IdnHost.Equals(expected.IdnHost, StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.TrimEnd('/') is var path && path.Length != 0 && path != expected.AbsolutePath
                && !(provider == PlaygroundProviderKind.DeepSeek && path == "/v1"))
            throw new ArgumentException("本候选仅连接所选服务商的官方 API 地址。", nameof(endpoint));
        return Default(provider);
    }
    public static bool SafePublicUrl(string? value)
    {
        if (value is null || value.Length > 8192 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0 || uri.Port is not (80 or 443)
            || uri.HostNameType == UriHostNameType.Unknown || uri.IsLoopback) return false;
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (!host.Contains('.') || host.EndsWith(".localhost", StringComparison.Ordinal)
            || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal)) return false;
        if (System.Net.IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            var bytes = address.GetAddressBytes();
            if (bytes.Length == 4) return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224
                && !(bytes[0] == 169 && bytes[1] == 254) && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 168) && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
            return !address.IsIPv6LinkLocal && !address.IsIPv6Multicast && !address.IsIPv6SiteLocal
                && (bytes[0] & 0xfe) != 0xfc && !address.Equals(System.Net.IPAddress.IPv6Any);
        }
        return true;
    }
}
