namespace TheOrder.Application;

public sealed record VndbAccount(string Id, string Username);

public sealed record VndbCredential(string Token, VndbAccount Account)
{
    public override string ToString() => "VNDB credential";
}

public interface IVndbCredentialStore
{
    Task<VndbCredential?> ReadAsync(CancellationToken cancellation);
    Task SaveAsync(VndbCredential? credential, CancellationToken cancellation);
}

public interface IVndbAccountProvider
{
    Task<VndbAccount> ValidateAsync(string token, CancellationToken cancellation);
    Task WriteAsync(CollectionUpdate update, string token, CancellationToken cancellation);
}

public sealed class VndbAuthorization(IVndbCredentialStore store, IVndbAccountProvider provider)
{
    private readonly SemaphoreSlim serial = new(1);
    private VndbCredential? saved;
    private bool initialized;
    public VndbAccount? Account => saved?.Account;
    public bool NeedsReauthorization { get; private set; }
    public VndbAccount? ConnectedAccount => NeedsReauthorization ? null : Account;

    public static string NormalizeToken(string? token)
    {
        var input = token?.Trim();
        if (input is not { Length: >= 32 and <= 48 }
            || input.Any(c => c is not (>= 'a' and <= 'z' or >= '1' and <= '9' or '-'))
            || input.Count(c => c != '-') != 32)
            throw new CollectionSyncException(CollectionSyncFailure.InvalidToken);
        return input;
    }

    public static bool ValidAccount(VndbAccount? account) => account is not null && CollectionUpdate.ValidVndbId(account.Id, 'u')
        && account.Username is { Length: > 0 and <= 200 } name && !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl);

    public Task InitializeAsync(CancellationToken cancellation) => RunAsync(async () =>
    {
        if (initialized) return;
        saved = await store.ReadAsync(cancellation).ConfigureAwait(false);
        initialized = true;
    }, cancellation);

    public Task SaveAsync(string token, CancellationToken cancellation) => RunAsync(async () =>
    {
        var normalized = NormalizeToken(token);
        var account = await provider.ValidateAsync(normalized, cancellation).ConfigureAwait(false);
        if (!ValidAccount(account)) throw new CollectionSyncException(CollectionSyncFailure.InvalidResponse);
        var credential = new VndbCredential(normalized, account);
        await store.SaveAsync(credential, cancellation).ConfigureAwait(false);
        saved = credential;
        initialized = true;
        NeedsReauthorization = false;
    }, cancellation);

    public Task TestAsync(CancellationToken cancellation) => RunAsync(async () =>
    {
        if (saved is not { } credential) throw new CollectionSyncException(CollectionSyncFailure.Unauthorized);
        var account = await RequireAuthorizationAsync(async () =>
        {
            var validated = await provider.ValidateAsync(credential.Token, cancellation).ConfigureAwait(false);
            if (!ValidAccount(validated) || validated.Id != credential.Account.Id)
                throw new CollectionSyncException(CollectionSyncFailure.Unauthorized);
            return validated;
        }).ConfigureAwait(false);
        if (account != credential.Account)
        {
            credential = credential with { Account = account };
            await store.SaveAsync(credential, cancellation).ConfigureAwait(false);
            saved = credential;
        }
        NeedsReauthorization = false;
    }, cancellation);

    public Task ClearAsync(CancellationToken cancellation) => RunAsync(async () =>
    {
        await store.SaveAsync(null, cancellation).ConfigureAwait(false);
        saved = null;
        initialized = true;
        NeedsReauthorization = false;
    }, cancellation);

    public Task WriteAsync(CollectionUpdate update, VndbAccount expected, CancellationToken cancellation) => RunAsync(async () =>
    {
        if (saved is not { } credential || !ReferenceEquals(ConnectedAccount, expected))
            throw new CollectionSyncException(CollectionSyncFailure.Changed);
        await RequireAuthorizationAsync(async () =>
        {
            await provider.WriteAsync(update, credential.Token, cancellation).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }, cancellation);

    private async Task<T> RequireAuthorizationAsync<T>(Func<Task<T>> request)
    {
        try { return await request().ConfigureAwait(false); }
        catch (CollectionSyncException error) when (error.Failure is CollectionSyncFailure.Unauthorized or CollectionSyncFailure.MissingPermission)
        { NeedsReauthorization = true; throw; }
    }

    private async Task RunAsync(Func<Task> action, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { await action().ConfigureAwait(false); }
        finally { serial.Release(); }
    }
}
