using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Playground;

namespace TheOrder.Platform.Windows;

// Separate from NextMoe credentials. No key enters observable state or chat exports.
public sealed class WindowsPlaygroundCredentials : IPlaygroundCredentials
{
    private readonly PlaygroundDisk disk;
    private readonly SemaphoreSlim serial = new(1, 1);
    private sealed record Document(int Version, PlaygroundIdentity Identity, Dictionary<string, string> Keys);
    public WindowsPlaygroundCredentials(NativeConfiguration configuration) => disk = new(configuration);
    private static string Slot(PlaygroundProviderKind provider, string endpoint) => provider + ":" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PlaygroundEndpointPolicy.Normalize(provider, endpoint))));
    public async Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) =>
        await ReadAsync(provider, endpoint, cancellation).ConfigureAwait(false) is not null;
    public async Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation)
    {
        var slot = Slot(provider, endpoint); await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await disk.WithDirectory(false, folder => Task.FromResult(folder is null ? null : Read(folder).Keys.GetValueOrDefault(slot)), cancellation).ConfigureAwait(false); }
        finally { serial.Release(); }
    }
    public async Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation)
    {
        var slot = Slot(provider, endpoint); key = key.Trim();
        if (key.Length is < 1 or > 8192 || key.Any(c => c < 0x21 || c > 0x7e)) throw new ArgumentException("API Key 格式无效，请检查后重新输入。");
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await disk.WithDirectory(true, async folder =>
            {
                var document = Read(folder!); document.Keys[slot] = key;
                await Write(folder!, document, cancellation).ConfigureAwait(false); return true;
            }, cancellation).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }
    public async Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation)
    {
        var slot = Slot(provider, endpoint); await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await disk.WithDirectory(false, async folder =>
            {
                if (folder is null) return false;
                var document = Read(folder); if (!document.Keys.Remove(slot)) return false;
                await Write(folder, document, cancellation).ConfigureAwait(false); return true;
            }, cancellation).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }
    private Document Read(string folder)
    {
        var encrypted = PlaygroundDisk.ReadBytes(Path.Combine(folder, "credentials-v1.dpapi"));
        if (encrypted is null) return new(1, disk.Identity, []);
        if (encrypted.Length > 128 * 1024) throw new InvalidDataException("Playground 凭据文件超出限制，原文件已保留。");
        byte[] plain;
        try { plain = WindowsDataProtection.Protect(encrypted, false); }
        catch (CryptographicException) { throw new InvalidDataException("Playground 凭据无法在当前 Windows 用户下解密，原文件已保留。"); }
        try
        {
            var document = JsonSerializer.Deserialize<Document>(plain, PlaygroundDisk.Json) ?? throw new InvalidDataException();
            if (document.Version != 1 || document.Identity != disk.Identity || document.Keys is null || document.Keys.Count > 32
                || document.Keys.Any(k => k.Key.Length > 128 || k.Value.Length is < 1 or > 8192 || k.Value.Any(c => c < 0x21 || c > 0x7e)))
                throw new InvalidDataException("Playground 凭据身份或格式不符合约定，原文件已保留。");
            return document;
        }
        catch (JsonException) { throw new InvalidDataException("Playground 凭据无法安全读取，原文件已保留。"); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private static async Task Write(string folder, Document document, CancellationToken cancellation)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(document, PlaygroundDisk.Json); byte[] encrypted;
        try { encrypted = WindowsDataProtection.Protect(plain, true); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            await WindowsAtomicFile.WriteAsync(Path.Combine(folder, "credentials-v1.dpapi"), encrypted).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }
}
