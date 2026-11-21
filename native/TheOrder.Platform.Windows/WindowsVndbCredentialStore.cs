using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

public sealed class WindowsVndbCredentialStore(NativeConfiguration configuration) : IVndbCredentialStore
{
    public const string FileName = "native-vndb-key.dpapi";
    private const int MaximumBytes = 8192;
    private sealed record Envelope(int Schema, Guid ProfileId, Guid StoreId, VndbCredential? Credential);
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public Task<VndbCredential?> ReadAsync(CancellationToken cancellation)
        => AccessAsync((profile, path) => Task.FromResult(Read(profile, path)), cancellation);

    public Task SaveAsync(VndbCredential? credential, CancellationToken cancellation)
        => AccessAsync(async (profile, path) =>
        {
            _ = Read(profile, path);
            var plain = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, credential), Options);
            byte[] encrypted;
            try { encrypted = WindowsDataProtection.Protect(plain, true); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            await WindowsAtomicFile.WriteAsync(path, encrypted, stage =>
            {
                if (stage is PersistenceStage.BeforeTempWrite or PersistenceStage.BeforeReplace)
                {
                    cancellation.ThrowIfCancellationRequested();
                    profile.ValidateAccess();
                    if (stage == PersistenceStage.BeforeReplace) _ = Read(profile, path);
                }
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            return true;
        }, cancellation);

    private static VndbCredential? Read(NativeProfileStore profile, string path)
    {
        try
        {
            using var input = IsolatedLibrarySource.OpenChecked(path);
            if (input.Length is < 1 or > MaximumBytes) throw new JsonException();
            var bytes = new byte[(int)input.Length];
            input.ReadExactly(bytes);
            var plain = WindowsDataProtection.Protect(bytes, false);
            try
            {
                var envelope = JsonSerializer.Deserialize<Envelope>(plain, Options);
                if (envelope is not { Schema: 1 } || envelope.ProfileId != profile.Manifest.ProfileId
                    || envelope.StoreId != profile.Manifest.StoreId) throw new JsonException();
                if (envelope.Credential is { } credential
                    && (!VndbAuthorization.ValidAccount(credential.Account) || VndbAuthorization.NormalizeToken(credential.Token) != credential.Token))
                    throw new JsonException();
                return envelope.Credential;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (FileNotFoundException) { return null; }
        catch (CollectionSyncException) { throw new CollectionSyncException(CollectionSyncFailure.Storage); }
    }

    private Task<T> AccessAsync<T>(Func<NativeProfileStore, string, Task<T>> action, CancellationToken cancellation)
        => Task.Run(async () =>
        {
            try
            {
                if (configuration.Profile is not NativeProfileStore profile) throw new CollectionSyncException(CollectionSyncFailure.Storage);
                var root = NativeProfileAccess.Validate(configuration);
                var held = IsolatedLibraryPersistence.PinDirectoryChain(root);
                try
                {
                    profile.ValidateAccess();
                    cancellation.ThrowIfCancellationRequested();
                    return await action(profile, Path.Combine(root, FileName)).ConfigureAwait(false);
                }
                finally { foreach (var handle in held) handle.Dispose(); }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException
                or JsonException or DataIsolationException or NativeProfileException)
            { throw new CollectionSyncException(CollectionSyncFailure.Storage); }
        }, cancellation);
}
