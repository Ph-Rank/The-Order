using System.Security.Cryptography;
using TheOrder.Application;
using TheOrder.Application.Migrations;

namespace TheOrder.Platform.Windows;

public sealed class WindowsLibraryMigrationSource(ILibraryMigrationAdapter adapter) : ILibraryMigrationSource
{
    public Task<ImportDataset> ReadAsync(string selectedFile, CancellationToken cancellation)
        => Task.Run(async () =>
        {
            var path = Path.GetFullPath(selectedFile);
            if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("POTATOVN_ZIP_REQUIRED");
            var parent = Path.GetDirectoryName(path) ?? throw new InvalidDataException("SOURCE_DIRECTORY_REQUIRED");
            // Reuse physical-path and protected-profile boundaries; never extract the archive or probe game paths.
            NativeProfilePaths.Validate(new(NativeProfileKind.Test, parent));
            var held = IsolatedLibraryPersistence.PinDirectoryChain(parent);
            try
            {
                await using var file = IsolatedLibrarySource.OpenChecked(path);
                if (file.Length > 512L * 1024 * 1024) throw new InvalidDataException("SOURCE_TOO_LARGE");
                var magic = new byte[4];
                if (await file.ReadAtLeastAsync(magic, 4, false, cancellation) != 4 || magic[0] != 'P' || magic[1] != 'K')
                    throw new InvalidDataException("POTATOVN_ZIP_REQUIRED");
                file.Position = 0;
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellation)); file.Position = 0;
                return await adapter.ReadAsync(file, hash, cancellation).ConfigureAwait(false);
            }
            finally { foreach (var handle in held) handle.Dispose(); }
        }, cancellation);
}
