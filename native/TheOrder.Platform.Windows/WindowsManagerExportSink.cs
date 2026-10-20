using TheOrder.Application;
using TheOrder.Application.Migrations;

namespace TheOrder.Platform.Windows;

public sealed class WindowsManagerExportSink(NativeConfiguration configuration) : IManagerExportSink
{
    public Task<string?> WriteAsync(string selectedPath, ManagerExportPackage package, CancellationToken cancellation) => Task.Run(async () =>
    {
        if (!Path.IsPathFullyQualified(selectedPath) || !selectedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请选择 ZIP 文件。");
        var target = Path.GetFullPath(selectedPath); var root = NativeProfileAccess.Validate(configuration);
        if (target.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请将兼容备份保存在资料目录以外。");
        var parent = Path.GetDirectoryName(target)!; NativeProfilePaths.CheckChain(parent);
        var bytes = package.Archive.ToArray();
        if (bytes.Length is < 4 or > 64 * 1024 * 1024 || bytes[0] != 'P' || bytes[1] != 'K') throw new InvalidDataException("兼容备份内容无效。");
        var held = IsolatedLibraryPersistence.PinDirectoryChain(parent);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var receipt = await WindowsAtomicFile.WriteAsync(target, bytes).ConfigureAwait(false);
            using var verify = IsolatedLibrarySource.OpenChecked(target);
            if (verify.Length != bytes.Length || !System.Security.Cryptography.SHA256.HashData(verify).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(bytes))) throw new InvalidDataException("导出后校验失败。");
            return receipt.Warning;
        }
        finally { foreach (var handle in held) handle.Dispose(); }
    }, cancellation);
}
