using System.Security.Cryptography;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

public sealed class WindowsFeedbackCredentials(NativeConfiguration configuration) : IFeedbackCredentialStore
{
    private const int MaximumBytes = 8192;
    private const string FileName = "native-feedback-github.dpapi";
    private sealed record Envelope(int Schema, Guid ProfileId, Guid StoreId, FeedbackCredential Credential);

    public Task<FeedbackCredential?> LoadAsync(CancellationToken cancellation)
        => AccessAsync<FeedbackCredential?>(async (profile, path) =>
        {
            Stream input;
            try { input = IsolatedLibrarySource.OpenChecked(path); }
            catch (FileNotFoundException) { return null; }
            using (input)
            {
                if (input.Length == 0) return null;
                if (input.Length > MaximumBytes) throw new JsonException();
                var encrypted = new byte[(int)input.Length];
                await input.ReadExactlyAsync(encrypted, cancellation).ConfigureAwait(false);
                var plain = WindowsDataProtection.Protect(encrypted, false);
                try
                {
                    var envelope = JsonSerializer.Deserialize<Envelope>(plain);
                    if (envelope is not { Schema: 1, Credential: { } credential } ||
                        envelope.ProfileId != profile.Manifest.ProfileId || envelope.StoreId != profile.Manifest.StoreId ||
                        !credential.IsValid()) throw new JsonException();
                    return credential;
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
        }, cancellation);

    public Task SaveAsync(FeedbackCredential credential, CancellationToken cancellation)
        => AccessAsync(async (profile, path) =>
        {
            if (!credential.IsValid()) throw new JsonException();
            var plain = JsonSerializer.SerializeToUtf8Bytes(
                new Envelope(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, credential));
            byte[] encrypted;
            try { encrypted = WindowsDataProtection.Protect(plain, true); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            if (encrypted.Length > MaximumBytes) throw new JsonException();
            await WriteAsync(profile, path, encrypted, cancellation).ConfigureAwait(false);
            return true;
        }, cancellation);

    public Task ClearAsync(CancellationToken cancellation)
        => AccessAsync(async (profile, path) =>
        {
            await WriteAsync(profile, path, ReadOnlyMemory<byte>.Empty, cancellation).ConfigureAwait(false);
            return true;
        }, cancellation);

    private static async Task WriteAsync(NativeProfileStore profile, string path, ReadOnlyMemory<byte> bytes,
        CancellationToken cancellation)
    {
        profile.ValidateAccess();
        cancellation.ThrowIfCancellationRequested();
        var receipt = await WindowsAtomicFile.WriteAsync(path, bytes).ConfigureAwait(false);
        if (receipt.Warning is not null) throw new FeedbackException(FeedbackFailure.Storage);
    }

    private Task<T> AccessAsync<T>(Func<NativeProfileStore, string, Task<T>> action, CancellationToken cancellation)
        => Task.Run(async () =>
        {
            try
            {
                var profile = configuration.Profile as NativeProfileStore ?? throw new FeedbackException(FeedbackFailure.Storage);
                var root = NativeProfileAccess.Validate(configuration);
                var held = IsolatedLibraryPersistence.PinDirectoryChain(root);
                try { return await action(profile, Path.Combine(root, FileName)).ConfigureAwait(false); }
                finally { foreach (var handle in held) handle.Dispose(); }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException
                or JsonException or DataIsolationException or NativeProfileException)
            {
                throw new FeedbackException(FeedbackFailure.Storage);
            }
        }, cancellation);
}
