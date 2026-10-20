using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using TheOrder.Application;
using TheOrder.Application.Migrations;

namespace TheOrder.Platform.Windows;

// Native database parsers run outside the UI process. This is crash/resource
// containment, not a security sandbox or permission to read any user Profile.
public sealed class WindowsIsolatedMigrationSource : ILibraryMigrationSource
{
    public const string Marker = "--manager-backup-reader";
    private const int MaximumReply = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 40 };
    public static bool IsInvocation(string[] args) => args.Contains(Marker, StringComparer.Ordinal);

    public static int Run(string[] args, ILibraryMigrationAdapter adapter)
    {
        // No malformed auxiliary invocation may fall through to normal startup.
        if (args.Length != 2 || args[0] != Marker || !Console.IsInputRedirected || !Console.IsOutputRedirected) return 64;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(85));
        try
        {
            // Parent assigns the memory/lifetime job before permitting parsing.
            if (Console.In.ReadLineAsync(timeout.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() != "read-v1") return 64;
            var data = new WindowsLibraryMigrationSource(adapter).ReadAsync(args[1], timeout.Token).GetAwaiter().GetResult();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
            if (bytes.Length > MaximumReply) return 65;
            using var output = Console.OpenStandardOutput(); output.Write(bytes); output.Flush(); return 0;
        }
        catch { return 66; } // No private fields, paths or exception payloads in stderr.
    }

    public async Task<ImportDataset> ReadAsync(string selectedFile, CancellationToken cancellation)
    {
        var path = Path.GetFullPath(selectedFile);
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("MANAGER_BACKUP_ZIP_REQUIRED");
        var parent = Path.GetDirectoryName(path) ?? throw new InvalidDataException("SOURCE_DIRECTORY_REQUIRED");
        NativeProfilePaths.Validate(new(NativeProfileKind.Test, parent));
        var held = IsolatedLibraryPersistence.PinDirectoryChain(parent);
        try
        {
            await using var source = IsolatedLibrarySource.OpenChecked(path);
            if (source.Length > 512L * 1024 * 1024) throw new InvalidDataException("SOURCE_TOO_LARGE");
            var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(source, cancellation));
            var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException())
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Marker); start.ArgumentList.Add(path);
            start.Environment.Remove("THE_ORDER_NATIVE_DATA_DIR"); start.Environment.Remove("THE_ORDER_NATIVE_PROFILE_KIND");
            start.Environment.Remove("THE_ORDER_NATIVE_FIXTURE_PROFILE");
            using var job = MigrationParserJob.Create();
            using var process = Process.Start(start) ?? throw new InvalidDataException("SOURCE_READER_START_FAILED");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                job.Assign(process);
                await process.StandardInput.WriteLineAsync("read-v1".AsMemory(), timeout.Token); process.StandardInput.Close();
                var output = ReadBounded(process.StandardOutput.BaseStream, MaximumReply, timeout.Token);
                var error = ReadBounded(process.StandardError.BaseStream, 4096, timeout.Token);
                await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
                if (process.ExitCode != 0) throw new InvalidDataException("SOURCE_READER_REJECTED_BACKUP");
                var data = JsonSerializer.Deserialize<ImportDataset>(await output, Json) ?? throw new InvalidDataException("SOURCE_READER_INVALID_REPLY");
                ImportModel.Validate(data);
                if (data.DocumentDigest != digest) throw new InvalidDataException("SOURCE_CHANGED_DURING_PREVIEW");
                return data;
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { throw new InvalidDataException("SOURCE_READER_TIME_LIMIT"); }
            finally
            {
                // Only our exact child process generation; never a name/PID search.
                if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(CancellationToken.None); }
            }
        }
        finally { foreach (var handle in held) handle.Dispose(); }
    }

    internal static async Task<byte[]> ReadBounded(Stream stream, int maximum, CancellationToken token)
    {
        using var output = new MemoryStream(); var buffer = new byte[65536]; int n;
        while ((n = await stream.ReadAsync(buffer, token)) != 0)
        { if (output.Length + n > maximum) throw new InvalidDataException("SOURCE_READER_REPLY_LIMIT"); await output.WriteAsync(buffer.AsMemory(0, n), token); }
        return output.ToArray();
    }
}

internal sealed class MigrationParserJob : IDisposable
{
    private readonly SafeFileHandle handle;
    private MigrationParserJob(SafeFileHandle handle) => this.handle = handle;
    internal static MigrationParserJob Create()
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException("SOURCE_READER_JOB_FAILED"); }
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 | 0x100 | 0x8, ActiveProcesses = 1 }, ProcessMemory = 768U * 1024 * 1024 };
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        { handle.Dispose(); throw new IOException("SOURCE_READER_JOB_FAILED"); }
        return new(handle);
    }
    internal void Assign(Process process)
    { if (!AssignProcessToJobObject(handle, process.Handle)) throw new IOException("SOURCE_READER_JOB_FAILED"); }
    public void Dispose() => handle.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    { public long ProcessTime, JobTime; public uint Flags; public nuint MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcesses; public nuint Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong Read, Write, Other, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
