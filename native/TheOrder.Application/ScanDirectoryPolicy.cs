using System.Collections.Immutable;

namespace TheOrder.Application;

public static class ScanDirectoryPolicy
{
    // These names are excluded only at a volume boundary, not in arbitrary game subfolders.
    internal static readonly ImmutableHashSet<string> VolumeDirectories = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
        "Program Files", "Program Files (x86)", "ProgramData", "Recovery", "Windows.old", "Boot", "EFI", "Config.Msi",
        "$Windows.~BT", "$Windows.~WS", "$Extend", "$Recycle.Bin", "System Volume Information", "Windows");

    public static string DosPath(string path) => path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
        : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    public static bool IsLocalDriveRoot(string path)
    {
        var value = DosPath(path).Replace('/', '\\');
        return value.Length == 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] == '\\';
    }
    public static bool IsDriveRoot(PathObservation path) => IsLocalDriveRoot(path.DisplayPath) || IsLocalDriveRoot(path.FinalPath);
    public static bool SkipChild(PathObservation parent, string name) => BoundedScanner.SkippedDirectories.Contains(name)
        || IsDriveRoot(parent) && VolumeDirectories.Contains(name);
    public static string CandidateName(PathObservation folder)
    {
        // A mount-point alias may resolve to a volume root. Keep its folder name; never
        // turn the entire local alias path into a title that could become a metadata query.
        var name = Path.GetFileName(folder.DisplayPath);
        return name.Length > 0 ? name : UiText.Format($"磁盘 {ScanDirectoryPolicy.DosPath(folder.DisplayPath)} 中的游戏");
    }
}
