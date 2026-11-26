using System.Security.Cryptography;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Playground;

namespace TheOrder.Platform.Windows;

// Deliberately separate from API keys, exported chats, portable backups and Codex credentials.
public sealed class WindowsChatGptAccountStore(NativeConfiguration configuration) : IChatGptAccountStore
{
    private readonly PlaygroundDisk disk = new(configuration);
    private readonly SemaphoreSlim serial = new(1, 1);
    private const string Name = "chatgpt-accounts-v1.dpapi";
    public PlaygroundIdentity Identity => disk.Identity;
    public async Task<ChatGptAccountState?> ReadAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await disk.WithDirectory(false, folder => Task.FromResult(folder is null ? null : Read(folder)), cancellation).ConfigureAwait(false); }
        finally { serial.Release(); }
    }
    public async Task SaveAsync(ChatGptAccountState state, long expectedRevision, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await disk.WithDirectory(true, async folder =>
            {
                Validate(state);
                var prior = Read(folder!);
                if ((prior?.Revision ?? 0) != expectedRevision || state.Revision != expectedRevision + 1
                    || prior is not null && prior.HostId != state.HostId)
                    throw new InvalidDataException("ChatGPT 连接已变化，请重新读取，原记录已保留。");
                byte[] plain = JsonSerializer.SerializeToUtf8Bytes(state, PlaygroundDisk.Json), encrypted;
                try { if (plain.Length > 4 * 1024 * 1024) throw new InvalidDataException("ChatGPT 连接资料超过容量限制。"); encrypted = WindowsDataProtection.Protect(plain, true); }
                finally { CryptographicOperations.ZeroMemory(plain); }
                try { cancellation.ThrowIfCancellationRequested(); await WindowsAtomicFile.WriteAsync(Path.Combine(folder!, Name), encrypted).ConfigureAwait(false); }
                finally { CryptographicOperations.ZeroMemory(encrypted); }
                return true;
            }, cancellation).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }
    private ChatGptAccountState? Read(string folder)
    {
        var encrypted = PlaygroundDisk.ReadBytes(Path.Combine(folder, Name)); if (encrypted is null) return null;
        if (encrypted.Length > 4 * 1024 * 1024 + 65536) throw new InvalidDataException("ChatGPT 连接资料大小无效，原文件已保留。");
        byte[]? plain = null;
        try
        {
            plain = WindowsDataProtection.Protect(encrypted, false);
            var state = JsonSerializer.Deserialize<ChatGptAccountState>(plain, PlaygroundDisk.Json) ?? throw new InvalidDataException();
            Validate(state); return state;
        }
        catch (Exception e) when (e is JsonException or CryptographicException)
        { throw new InvalidDataException("ChatGPT 连接资料无法安全读取，原文件已保留。"); }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }
    private void Validate(ChatGptAccountState state)
    {
        if (state.Version != 1 || state.Revision < 0 || state.Identity != Identity || !ChatGptSignInPolicy.ValidHost(state.HostId)
            || state.Accounts.IsDefault || state.Accounts.Length > 16 || state.Accounts.Any(a => a is null)
            || state.Accounts.Select(a => a.ClientId).Distinct(StringComparer.Ordinal).Count() != state.Accounts.Length
            || state.SelectedClientId is not null && !state.Accounts.Any(a => a.ClientId == state.SelectedClientId)
            || state.PendingClientId is not null && !ChatGptSignInPolicy.ValidClient(state.PendingClientId)) throw Invalid();
        foreach (var a in state.Accounts)
            if (!ChatGptSignInPolicy.ValidClient(a.ClientId) || string.IsNullOrWhiteSpace(a.Subject) || a.Subject.Length > 1024
                || a.Label is null || a.Label.Length is < 1 or > 300 || a.Label.Any(char.IsControl) || a.Email is { Length: > 240 }
                || a.Email?.Any(char.IsControl) == true || a.Scopes.IsDefault || a.Scopes.Length > 64
                || a.Scopes.Any(s => s is null || s.Length is < 1 or > 128 || s.Any(c => c < 0x21 || c > 0x7e))
                || new[] { a.AccessToken, a.RefreshToken, a.IdToken }.Any(t => t is not null && !ChatGptSignInPolicy.SafeToken(t))) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("ChatGPT 连接身份或格式无效，原文件已保留。");
}
