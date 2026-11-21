using System.Collections.Concurrent;
using System.Collections.Immutable;
using TheOrder.Application;

namespace TheOrder.WinUI.Startup;

// Composition requires an owned Test profile + visual + diagnostics + this explicit opt-in.
// Never opens a browser, talks to NextMoe, or represents real-account acceptance.
internal sealed class SyntheticNextMoe : IOAuthLogin, INextMoeOAuth, INextMoePersonalApi, INextMoeCollectionWriter
{
    private string[] scopes = [];
    private readonly Dictionary<string, long> minutes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CloudWorkState> states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CloudRating> ratings = new(StringComparer.Ordinal)
    {
        ["86"] = new("86", 9, "2026-10-01T00:00:00Z")
    };
    private readonly HashSet<string> members = new(StringComparer.Ordinal);
    public Task<OAuthAuthorizationCode> AuthorizeAsync(string id, string[] permissions, CancellationToken cancellation)
    { cancellation.ThrowIfCancellationRequested(); scopes = [.. permissions]; return Task.FromResult(new OAuthAuthorizationCode("synthetic-code", "synthetic-verifier", new("http://127.0.0.1:12345/callback"))); }
    private OAuthTokens Tokens() => new("synthetic-only", "synthetic-refresh-test", DateTimeOffset.UtcNow.AddMinutes(15), scopes.Length == 0 ? [.. NextMoeOAuthIdentity.LoginScopes] : [.. scopes], new("42", "合成 NextMoe 用户"));
    public Task<OAuthTokens> ExchangeAsync(string id, OAuthAuthorizationCode code, CancellationToken cancellation) => Task.FromResult(Tokens());
    public Task<OAuthTokens> RefreshAsync(string id, string refresh, CancellationToken cancellation) => Task.FromResult(Tokens());
    public Task<CloudPage<CloudPlaytime>> PlaytimesAsync(string? cursor, CancellationToken cancellation) => Task.FromResult(new CloudPage<CloudPlaytime>([.. minutes.Select(x => new CloudPlaytime(x.Key, x.Value))], null, false));
    public Task<CloudPage<CloudWorkState>> StatesAsync(string? cursor, CancellationToken cancellation) => Task.FromResult(new CloudPage<CloudWorkState>([.. states.Values], null, false));
    public Task<CloudPage<CloudRating>> RatingsAsync(string? cursor, CancellationToken cancellation)
        => Task.FromResult(new CloudPage<CloudRating>([.. ratings.Values], null, false));
    public Task<CloudPage<CloudFolder>> FoldersAsync(string? cursor, CancellationToken cancellation) => Task.FromResult(new CloudPage<CloudFolder>([new("10", "合成收藏夹", "42", members.Count, DateTimeOffset.UtcNow.ToString("o"))], null, false));
    public Task<CloudPage<string>> FolderItemsAsync(string id, string? cursor, CancellationToken cancellation) => Task.FromResult(new CloudPage<string>([.. members], null, false));
    public Task<ImmutableArray<PersonalWriteResult>> WriteAsync(PersonalSyncKind kind, string? folder, ImmutableArray<PersonalWrite> items, string key, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        foreach (var item in items)
            if (kind == PersonalSyncKind.Playtime) minutes[item.WorkId] = item.Minutes!.Value;
            else if (kind == PersonalSyncKind.State) states[item.WorkId] = new(item.WorkId, item.State!, item.Completion, DateTimeOffset.UtcNow.ToString("o"));
            else if (kind == PersonalSyncKind.Folder) members.Add(item.WorkId); else throw new InvalidOperationException("No rating writes.");
        return Task.FromResult<ImmutableArray<PersonalWriteResult>>([.. items.Select(x => new PersonalWriteResult(x.WorkId, true, 200, null))]);
    }
    internal static string? MergeTarget(string id)
        => Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_NEXTMOE_MERGE_FIXTURE") == "1" ? id switch
        {
            "900186" => "900190",
            "900286" => "900290",
            "900386" => "900390",
            _ => null
        } : null;

    public Task<NextMoeCollectionResult> WriteAsync(CollectionUpdate update, OnlineAuthSnapshot expected, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        expected.Changed.ThrowIfCancellationRequested();
        if (MergeTarget(update.WorkId) is { } target)
            return Task.FromResult(new NextMoeCollectionResult(false, false, $"已合并至 #{target}。", target));
        var stamp = DateTimeOffset.UtcNow.ToString("o");
        if (update.Status is { } status)
            states[update.WorkId] = new(update.WorkId, status switch { 1 => "wish", 2 => "done", 3 => "doing", 4 => "on_hold", _ => "dropped" }, null, stamp);
        if (update.Rating is { } rating)
            ratings[update.WorkId] = new(update.WorkId, rating, stamp);
        return Task.FromResult(new NextMoeCollectionResult(update.Status.HasValue, update.Rating.HasValue, null));
    }

    public Task<CloudFolder> CreateFolderAsync(string name, string key, CancellationToken cancellation) => throw new OnlineException(OnlineFailure.Forbidden);
    public Task DeleteFolderItemAsync(string folder, string work, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); members.Remove(work); return Task.CompletedTask; }
}
