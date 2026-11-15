using System.Globalization;

namespace TheOrder.Application;

public enum FeedbackKind { Problem, Suggestion }

public sealed record FeedbackEnvironment(string Version, string WindowsVersion, int ScalePercent);

public sealed record FeedbackReport(string Title, string Body)
{
    public const int TitleLimit = 100;
    public const int DescriptionLimit = 6000;
    public const int DiagnosticLimit = 1800;

    public static FeedbackReport Create(FeedbackKind kind, string title, string description,
        FeedbackEnvironment environment, string? diagnostics)
    {
        title = title.Trim();
        description = description.Trim();
        if (title.Length is 0 or > TitleLimit || title.Any(char.IsControl) ||
            description.Length is 0 or > DescriptionLimit || diagnostics?.Length > DiagnosticLimit)
            throw new FeedbackException(FeedbackFailure.InvalidReport);

        var heading = UiText.Get(kind == FeedbackKind.Problem ? "问题描述" : "功能建议");
        var body = $"## {heading}\n\n{description}\n\n" +
            $"## {UiText.Get("环境")}\n\n- The Order {environment.Version}\n- Windows {environment.WindowsVersion}\n" +
            $"- {UiText.Get("窗口缩放")} {environment.ScalePercent.ToString(CultureInfo.InvariantCulture)}%";
        if (!string.IsNullOrWhiteSpace(diagnostics))
            body += $"\n\n## {UiText.Get("近期运行诊断")}\n\n```text\n{diagnostics}\n```";
        return new(title, body);
    }
}

public enum FeedbackFailure
{
    NotConfigured, SignInRequired, AuthorizationExpired, AuthorizationDenied,
    RateLimited, RepositoryUnavailable, ServiceUnavailable, InvalidReport, SubmissionUnknown, Storage
}

public sealed class FeedbackException(FeedbackFailure failure) : Exception(failure.ToString())
{
    public FeedbackFailure Failure { get; } = failure;
}

// The profile store encrypts this envelope. Never include it in logs or feedback.
public sealed class FeedbackCredential(string clientId, string login, string accessToken, string refreshToken,
    DateTimeOffset accessExpires, DateTimeOffset refreshExpires)
{
    public string ClientId { get; } = clientId;
    public string Login { get; } = login;
    public string AccessToken { get; } = accessToken;
    public string RefreshToken { get; } = refreshToken;
    public DateTimeOffset AccessExpires { get; } = accessExpires;
    public DateTimeOffset RefreshExpires { get; } = refreshExpires;

    public static bool ValidClientId(string? value) => value is { Length: > 0 and <= 100 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    public static bool ValidLogin(string? value) => value is { Length: > 0 and <= 100 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public bool IsValid() => ValidClientId(ClientId) && ValidLogin(Login)
        && ValidToken(AccessToken, "ghu_") && ValidToken(RefreshToken, "ghr_")
        && AccessExpires != default && RefreshExpires >= AccessExpires;

    private static bool ValidToken(string? value, string prefix) => value is { Length: > 4 and <= 1024 }
        && value.StartsWith(prefix, StringComparison.Ordinal)
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}

public interface IFeedbackCredentialStore
{
    Task<FeedbackCredential?> LoadAsync(CancellationToken cancellation);
    Task SaveAsync(FeedbackCredential credential, CancellationToken cancellation);
    Task ClearAsync(CancellationToken cancellation);
}
