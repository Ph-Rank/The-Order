using TheOrder.Application;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace TheOrder.Platform.Windows;

public sealed class WindowsScanFilesystem : IScanFilesystem
{
    public IEnumerable<DirectoryItem> Enumerate(PathObservation directory, PathObservation authorizedRoot)
    {
        var leases = new List<SafeFileHandle>();
        try
        {
            // Pin lexical components without following their final reparse point. Denying write/delete
            // sharing prevents replacement/retargeting while the top-level enumerator is active.
            foreach (var start in new[] { directory.DisplayPath, directory.FinalPath })
            for (string? part = start; part is not null; part = Path.GetDirectoryName(part))
            {
                var handle = CreateFile(part, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException(UiText.Get("目录正在变化或无法安全枚举，请稍后重试。")); }
                leases.Add(handle);
            }
            var identity = new WindowsFilesystemIdentity();
            var rootNow = identity.Inspect(authorizedRoot.StoredPath); var current = identity.Inspect(directory.StoredPath);
            if (!rootNow.IsValid || !InstallationPreflightService.SameObservation(authorizedRoot, rootNow.Observation!)
                || !current.IsValid || !InstallationPreflightService.SameObservation(directory, current.Observation!))
                throw new InvalidDataException(UiText.Get("枚举前目录身份已变化，扫描已停止。请重新选择目录。"));
            foreach (var entry in Enumerate(directory.StoredPath)) yield return entry;
        }
        finally { foreach (var handle in leases) handle.Dispose(); }
    }
    public IEnumerable<DirectoryItem> Enumerate(string directory)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false };
        foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
            yield return new(item.FullName, item.Name, (item.Attributes & FileAttributes.Directory) != 0, (item.Attributes & FileAttributes.ReparsePoint) != 0);
    }
    public bool IsForbiddenRoot(PathObservation root)
    {
        return IsForbiddenPath(root.FinalPath, Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            [Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)]);
    }
    internal static bool IsForbiddenPath(string path, string windows, IEnumerable<string> specialFolders)
    {
        var final = Path.TrimEndingDirectorySeparator(ScanDirectoryPolicy.DosPath(path));
        var drive = Path.GetPathRoot(final);
        bool equal(string? a, string? b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
            && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
        // A data volume is an explicit bounded scan root. The system volume and UNC share roots
        // remain blocked; aliases are judged by the actual resolved target, not their drive letter.
        if (equal(drive, final) && (!ScanDirectoryPolicy.IsLocalDriveRoot(final) || equal(drive, Path.GetPathRoot(windows)))) return true;
        if (equal(windows, final) || !string.IsNullOrEmpty(windows) && InstallationPreflightService.Within(final, windows)) return true;
        return specialFolders.Any(p => equal(p, final));
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
