using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application.Migrations;

public sealed record ManagerExportNotice(string Field, string Message, int Count);
public sealed record ManagerExportPackage(string Manager, int Entries, ReadOnlyMemory<byte> Archive,
    ImmutableArray<ManagerExportNotice> Notices)
{
    public string Sha256 => ImportHash.Bytes(Archive.Span);
}
public interface IManagerArchiveWriter
{
    Task<ManagerExportPackage> CreateAsync(string manager, ImmutableArray<LibraryEntry> entries, CancellationToken cancellation);
}
public interface IManagerExportSink
{
    Task<string?> WriteAsync(string selectedPath, ManagerExportPackage package, CancellationToken cancellation);
}
public sealed class LibraryManagerExportService(OfflineLibraryClient library, ILibraryCodec codec,
    IManagerArchiveWriter writer, IManagerExportSink sink)
{
    public async Task<ManagerExportPackage> PreviewAsync(string manager, CancellationToken cancellation)
    {
        var snapshot = await library.ExportBackupAsync(cancellation).ConfigureAwait(false);
        return await writer.CreateAsync(manager, codec.Decode(snapshot), cancellation).ConfigureAwait(false);
    }
    public Task<string?> SaveAsync(string path, ManagerExportPackage package, CancellationToken cancellation)
        => sink.WriteAsync(path, package, cancellation);
}
