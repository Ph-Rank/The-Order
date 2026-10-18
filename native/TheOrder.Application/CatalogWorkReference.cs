namespace TheOrder.Application;

// Parse a supported browsing link into an ID. Never request the supplied URL.
public static class CatalogWorkReference
{
    public static bool TryParse(string? value, out string id)
    {
        id = ""; var input = value?.Trim();
        static bool Valid(string? x) => x is { Length: >= 1 and <= 20 } && x[0] is >= '1' and <= '9' && x.All(char.IsAsciiDigit);
        if (Valid(input)) { id = input!; return true; }
        if (input is not { Length: <= 2048 } || input.Contains('%') || input.Contains('\\')
            || !Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || input != uri.AbsoluteUri) return false;
        var prefix = uri.Host switch { "www.kungal.com" or "kungal.com" => "/galgame/", "api.nextmoe.dev" => "/v2/catalog/works/", _ => "" };
        if (prefix.Length == 0 || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var part = uri.AbsolutePath[prefix.Length..].TrimEnd('/');
        if (!Valid(part) || uri.AbsolutePath != prefix + part && uri.AbsolutePath != prefix + part + "/") return false;
        id = part; return true;
    }
}
