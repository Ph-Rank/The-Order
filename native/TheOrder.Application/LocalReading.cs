using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed record LocalReadingFile(string Path, string Name, string Kind, string InstallationId = "");
public sealed record LocalReadingFiles(ImmutableArray<LocalReadingFile> Guides, ImmutableArray<LocalReadingFile> Notes);
public sealed record ReadingBlock(string Kind, string Text, ImmutableArray<ImmutableArray<string>> Rows = default, byte[]? Image = null);
public sealed record ReadingPage(string Label, ImmutableArray<ReadingBlock> Blocks, int ImageWidth = 0, int ImageHeight = 0, byte[]? Rgba = null);
public interface ILocalReadingDocument : IDisposable
{
    string Name { get; }
    string Notice { get; }
    int PageCount { get; }
    Task<ReadingPage> PageAsync(int index, CancellationToken cancellation);
}
public interface ILocalDocumentParser
{
    ImmutableArray<ReadingPage> Parse(string extension, byte[] bytes, CancellationToken cancellation);
}
public interface ILocalReadingLibrary
{
    Task<LocalReadingFiles> FindAsync(LibraryEntry entry, CancellationToken cancellation);
    Task<ILocalReadingDocument> OpenAsync(LocalReadingFile file, CancellationToken cancellation);
}
public sealed record GuideLocalReading(int Channel, ImmutableArray<string> Files, int File, int Page, int Pages, ReadingPage? Content, string Notice = "", bool HasNetwork = false, bool HasGuides = false, bool HasNotes = false, float Progress = 0, float Zoom = 1);
public sealed record LocalReadingPosition(int Page, float Progress, float Zoom, long At);
