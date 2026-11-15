using TheOrder.Application;

namespace TheOrder.WinUI.Startup;

// Isolated visual preview only; offers no executable and cannot install anything.
internal sealed class SyntheticUpdates : IAppUpdateChannelFeed
{
    public Task<AppUpdateRelease?> LatestAsync(CancellationToken cancellation) => LatestAsync(AppUpdateChannel.Stable, cancellation);
    public async Task<AppUpdateRelease?> LatestAsync(AppUpdateChannel channel, CancellationToken cancellation)
    {
        await Task.Delay(500, cancellation);
        var version = channel == AppUpdateChannel.Beta ? "1.9.0-beta.1" : "1.8.0";
        return new(version, "合成通道预览；测试资料不能执行安装。",
            new($"https://github.com/Ph-Rank/The-Order/releases/download/v{version}/The-Order-{version}-Setup-x64.exe"), 72000000, new string('0',64)) { ChannelProtocol = 1 };
    }
    public Task CopyInstallerAsync(AppUpdateRelease release, Stream destination, IProgress<AppUpdateTransfer> progress, CancellationToken cancellation)
        => throw new AppUpdateException("合成预览不下载安装包。");
}
