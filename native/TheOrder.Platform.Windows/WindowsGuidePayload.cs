using System.Security.Cryptography;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Hold the exact installed payload through preparation/loading; never patch files in a game folder.
internal sealed class WindowsGuidePayload : IDisposable
{
    private readonly List<IDisposable> leases = [];
    public string Root { get; }
    public WindowsGuidePayload(string root)
    {
        Root = Path.GetFullPath(root);
        try
        {
            CheckDirectory(Root);
            leases.AddRange(IsolatedLibraryPersistence.PinDirectoryChain(Root));
            using var manifest = IsolatedLibrarySource.OpenChecked(Path.Combine(Root, "payload.json"));
            if (manifest.Length > 128 * 1024) throw new InvalidDataException();
            using var json = JsonDocument.Parse(manifest);
            var entries = json.RootElement.GetProperty("files").EnumerateArray().ToArray();
            if (entries.Length is < 6 or > 128) throw new InvalidDataException();
            string[] required = ["x86/TheOrder.GuideHost.exe", "x64/TheOrder.GuideHost.exe",
                "x86/TheOrder.GuideRuntime.dll", "x64/TheOrder.GuideRuntime.dll", "TheOrder.Guide.addon32", "TheOrder.Guide.addon64"];
            foreach (var relative in required)
            {
                var entry = entries.Single(e => e.GetProperty("path").GetString() == relative);
                var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
                CheckDirectory(Path.GetDirectoryName(path)!);
                leases.AddRange(IsolatedLibraryPersistence.PinDirectoryChain(Path.GetDirectoryName(path)!));
                var stream = IsolatedLibrarySource.OpenChecked(path); leases.Add(stream);
                if (stream.Length != entry.GetProperty("bytes").GetInt64() || stream.Length > 32 * 1024 * 1024
                    || !Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Guide component integrity check failed");
            }
        }
        catch { Dispose(); throw; }
    }
    public void Dispose() { foreach (var lease in leases) lease.Dispose(); leases.Clear(); }
    internal static void CheckDirectory(string root)
    {
        if (!Path.IsPathFullyQualified(root)) throw new DataIsolationException("Guide directory must be absolute");
        for (DirectoryInfo? directory = new(Path.GetFullPath(root)); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new DataIsolationException("Guide directory cannot pass through a link");
    }
    internal static bool AllowsNative(LaunchSnapshot snapshot)
    {
        if (snapshot.LocaleTool is not null || snapshot.Installation.Compatibility?.UseSteam == true
            || snapshot.Installation.Compatibility?.Administrator == true
            || !Path.GetExtension(snapshot.Executable.StoredPath).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var directory = Path.GetDirectoryName(snapshot.Executable.FinalPath)!;
        // Existing wrappers keep their load order and files. They can still use the desktop reader.
        string[] wrappers = ["ReShade.ini", "d3d9.dll", "dxgi.dll", "d3d11.dll", "opengl32.dll", "ddraw.dll"];
        return !wrappers.Any(name => File.Exists(Path.Combine(directory, name)));
    }
}
