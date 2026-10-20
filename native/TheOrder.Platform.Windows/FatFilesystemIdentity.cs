using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// This is a local observation, not an exported or portable ID. FAT file indices can change
// on rename/defragmentation; reinspection must invalidate old candidates rather than guess.
internal static class FatFilesystemIdentity
{
    internal static bool AllowsFallback(int error, string fileSystem, uint flags) => error is 1 or 50 or 87
        && (flags & 0x80) == 0 // FILE_SUPPORTS_REPARSE_POINTS
        && (fileSystem.Equals("exFAT", StringComparison.OrdinalIgnoreCase) || fileSystem.Equals("FAT32", StringComparison.OrdinalIgnoreCase)
            || fileSystem.Equals("FAT", StringComparison.OrdinalIgnoreCase));

    internal static FilesystemIdentity? Create(string guidPath, uint serial, uint attributes, ulong index, ulong creation)
    {
        const string prefix = @"\\?\Volume{";
        var end = prefix.Length + 36;
        if (!guidPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || guidPath.Length < end + 2
            || guidPath[end] != '}' || guidPath[end + 1] != '\\'
            || !Guid.TryParseExact(guidPath.AsSpan(prefix.Length, 36), "D", out var volume) || volume == Guid.Empty
            || (attributes & 0x400) != 0) return null;
        bool root = guidPath.Length == end + 2 && (attributes & 0x10) != 0;
        if (!root && (index == 0 || creation == 0)) return null;
        // The volume GUID prevents drive-letter reuse from retaining an old authorization.
        // Creation time distinguishes recycled FAT slots; the namespace never collides with FILE_ID_INFO.
        return new($"FAT:{volume:D}:{serial:X8}", root ? "ROOT" : $"L64:{index:X16}:{creation:X16}");
    }
}
