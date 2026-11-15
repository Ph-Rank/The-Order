using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Stored separately from the shared UI preferences so older builds can still open the profile.
public sealed class WindowsClosePreference(NativeConfiguration configuration)
{
    private const string FileName = "native-window-close-v1.dat";
    private readonly SemaphoreSlim serial = new(1, 1);

    public Task<bool> ReadAsync() => WithFileAsync(path => Task.FromResult(Read(path)));

    public Task SaveAsync(bool exit) => WithFileAsync(async path =>
    {
        if (Read(path) != exit)
            await WindowsAtomicFile.WriteAsync(path, new byte[] { exit ? (byte)'1' : (byte)'0' }).ConfigureAwait(false);
        return exit;
    });

    private static bool Read(string path)
    {
        try
        {
            using var stream = IsolatedLibrarySource.OpenChecked(path);
            if (stream.Length == 1)
            {
                var value = stream.ReadByte();
                if (value == '0') return false;
                if (value == '1') return true;
            }
            throw new InvalidDataException("Unrecognized window close preference.");
        }
        catch (FileNotFoundException) { return false; }
    }

    private async Task<T> WithFileAsync<T>(Func<string, Task<T>> operation)
    {
        await serial.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                var root = NativeProfileAccess.Validate(configuration);
                var held = IsolatedLibraryPersistence.PinDirectoryChain(root);
                try
                {
                    NativeProfileAccess.Validate(configuration);
                    return await operation(Path.Combine(root, FileName)).ConfigureAwait(false);
                }
                finally { foreach (var handle in held) handle.Dispose(); }
            }).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }
}
