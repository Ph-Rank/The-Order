using System.Collections.Immutable;
namespace TheOrder.Application;
public sealed record LaunchFileChoice(string Path, string Label, PathObservation Observation);
public sealed record LaunchFiles(ImmutableArray<LaunchFileChoice> Choices, bool Partial, string Message)
{
    // Keep support tools in the explicit list, but never auto-start a lone
    // uninstaller, setup or configuration tool as the user's game.
    public LaunchFileChoice? AutomaticChoice => !Partial && !Choices.IsDefaultOrEmpty && Choices.Length == 1 &&
        (BoundedScanner.IsGameExecutable(Path.GetFileName(Choices[0].Path)) || BoundedScanner.IsLauncherScript(Path.GetFileName(Choices[0].Path))) ? Choices[0] : null;
}
// Reads only the user-selected installation; no file is executed.
public sealed class LaunchFileDiscovery(IFilesystemIdentity identity, IScanFilesystem filesystem)
{
    public static bool IsLaunchFile(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
    public Task<LaunchFiles> ReadAsync(string folder, CancellationToken cancellation = default) => Task.Run(() => Read(folder, cancellation), cancellation);
    private LaunchFiles Read(string folder, CancellationToken cancellation)
    {
        var root = identity.Inspect(folder);
        if (!root.IsValid) return new([], false, root.Message);
        var authorized = root.Observation!;
        if (filesystem.IsForbiddenRoot(authorized)) return new([], true, UiText.Get("请选择游戏安装目录或数据盘，不能浏览系统盘根目录、系统目录或网络共享根目录。"));
        var stack = new Stack<(PathObservation Path, int Depth)>(); stack.Push((authorized, 0));
        var seen = new HashSet<FilesystemIdentity>(); var choices = new List<LaunchFileChoice>();
        int entries = 0, directories = 0; bool partial = false;
        while (stack.TryPop(out var next))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!seen.Add(next.Path.Identity)) continue;
            if (++directories > 500) { partial = true; break; }
            if (!Current(authorized) || !Current(next.Path)) return Changed();
            try
            {
                foreach (var item in filesystem.Enumerate(next.Path, authorized))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++entries > 5000) { partial = true; break; }
                    if (item.IsReparsePoint) { partial = true; continue; }
                    if (item.IsDirectory)
                    {
                        if (ScanDirectoryPolicy.SkipChild(next.Path, item.Name)) continue;
                        if (next.Depth >= 6) { partial = true; continue; }
                        var directory = identity.Inspect(item.Path);
                        if (directory.IsValid && InScope(directory.Observation!))
                        { if (!filesystem.IsForbiddenRoot(directory.Observation!)) stack.Push((directory.Observation!, next.Depth + 1)); }
                        else partial = true;
                    }
                    else if (IsLaunchFile(item.Name))
                    {
                        var file = identity.Inspect(item.Path, false);
                        if (!file.IsValid || !InScope(file.Observation!)) { partial = true; continue; }
                        choices.Add(new(item.Path, System.IO.Path.GetRelativePath(folder, item.Path), file.Observation!));
                        if (choices.Count >= 200) { partial = true; break; }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { partial = true; }
            if (!Current(authorized) || !Current(next.Path)) return Changed();
            if (entries > 5000 || choices.Count >= 200) break;
        }
        return new([.. choices.DistinctBy(c => c.Observation.Identity).OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)], partial,
            partial ? UiText.Get("部分目录未能读取或达到扫描上限。可从本安装目录继续浏览选择。") : choices.Count == 0 ? UiText.Get("本目录中没有可读取的 exe 或 cmd 文件。") : "");
        bool Current(PathObservation expected)
        {
            var actual = identity.Inspect(expected.StoredPath);
            return actual.IsValid && InstallationPreflightService.SameObservation(expected, actual.Observation!);
        }
        bool InScope(PathObservation value) => value.Identity.Volume == authorized.Identity.Volume
            && InstallationPreflightService.Within(value.FinalPath, authorized.FinalPath)
            && value.ViaReparsePoint == authorized.ViaReparsePoint && value.ReparseTag == authorized.ReparseTag
            && value.ReparsePaths.SequenceEqual(authorized.ReparsePaths, StringComparer.OrdinalIgnoreCase);
        static LaunchFiles Changed() => new([], true, UiText.Get("安装目录在读取期间发生变化，请重新选择启动文件。"));
    }
}
