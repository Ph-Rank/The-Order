using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Playground;

namespace TheOrder.Platform.Windows;

// A tiny optional-entry preference can be read without opening chat, credentials or providers.
public sealed class WindowsPlaygroundNavigation(NativeConfiguration configuration)
{
    private readonly PlaygroundDisk disk = new(configuration);
    private readonly SemaphoreSlim serial = new(1, 1);
    private sealed record Preference(int Version, PlaygroundIdentity Identity, bool Visible);
    public Task<bool> ReadVisibleAsync(CancellationToken cancellation = default) => disk.WithDirectory(false, folder =>
    {
        var data = folder is null ? null : PlaygroundDisk.ReadBytes(Path.Combine(folder, "navigation-v1.json"));
        if (data is null) return Task.FromResult(true);
        if (data.Length > 4096) throw new InvalidDataException("Playground 入口设置过大，原文件已保留。");
        var value = JsonSerializer.Deserialize<Preference>(data, PlaygroundDisk.Json);
        if (value is null || value.Version != 1 || value.Identity != disk.Identity)
            throw new InvalidDataException("Playground 入口设置不属于当前资料环境或版本不受支持。");
        return Task.FromResult(value.Visible);
    }, cancellation);
    public async Task SetVisibleAsync(bool visible, CancellationToken cancellation = default)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            // Refuse to overwrite unknown or cross-profile state.
            await ReadVisibleAsync(cancellation).ConfigureAwait(false);
            await disk.WithDirectory(true, async folder =>
            {
                var data = JsonSerializer.SerializeToUtf8Bytes(new Preference(1, disk.Identity, visible));
                cancellation.ThrowIfCancellationRequested();
                await WindowsAtomicFile.WriteAsync(Path.Combine(folder!, "navigation-v1.json"), data).ConfigureAwait(false);
                return true;
            }, cancellation).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }
}
