using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Kernel membership is established atomically at creation, before user code runs.
// No limits, kill-on-close, breakaway flags, completion ports or inherited handles.
// This proves the lifetime of the owned CreateProcess descendant scope, not external brokers.
internal sealed class WindowsProcessLifetimeScope(SafeFileHandle job) : IProcessLifetimeScope
{
    public ProcessLifetimeObservation Query()
    {
        if (!QueryInformationJobObject(job, 1, out Accounting info, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero))
            return new(false, 0, 0);
        var buffer = Marshal.AllocHGlobal(8 + 8192 * IntPtr.Size);
        try
        {
            if (!QueryInformationJobObject(job, 3, buffer, 8 + 8192u * (uint)IntPtr.Size, IntPtr.Zero)) return new(false, info.Active, info.Total);
            int count = Marshal.ReadInt32(buffer, 4);
            if (count < 0 || count > 8192) return new(false, info.Active, info.Total);
            var ids = System.Collections.Immutable.ImmutableArray.CreateBuilder<int>(count);
            for (int i = 0; i < count; i++) ids.Add(checked((int)Marshal.ReadIntPtr(buffer, 8 + i * IntPtr.Size)));
            if (!QueryInformationJobObject(job, 1, out Accounting after, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero)) return new(false, 0, 0);
            return Reconcile(info.Active, info.Total, ids.ToImmutable(), after.Active, after.Total);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    // Membership and accounting are separate native calls. A birth/death between
    // them must never create a false sole-survivor checkpoint or empty scope.
    internal static ProcessLifetimeObservation Reconcile(uint beforeActive, uint beforeTotal,
        System.Collections.Immutable.ImmutableArray<int> ids, uint afterActive, uint afterTotal) =>
        beforeActive == afterActive && beforeTotal == afterTotal && ids.Length == afterActive
            ? new(true, afterActive, afterTotal) { ActivePids = ids }
            : new(false, afterActive, afterTotal);
    public WindowsProcessLifetimeScope Retain()
    {
        var self = GetCurrentProcess();
        if (!DuplicateHandle(self, job, self, out var copy, 0, false, 2)) throw new Win32Exception();
        return new(copy);
    }
    public void Dispose() => job.Dispose();

    internal static (SafeProcessHandle Handle, int Pid, WindowsProcessLifetimeScope Scope) Start(string executable, IEnumerable<string> arguments, string cwd, WindowsCommandScript? script = null, bool highDpi = false,
        IReadOnlyDictionary<string, string>? companionEnvironment = null, Action<int, long>? beforeResume = null)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); throw new Win32Exception(); }
        IntPtr attributes = IntPtr.Zero, jobList = IntPtr.Zero, environment = IntPtr.Zero;
        bool initialized = false, transferred = false;
        try
        {
            nuint bytes = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
            attributes = Marshal.AllocHGlobal(checked((int)bytes));
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) throw new Win32Exception();
            initialized = true;
            jobList = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(jobList, job.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(attributes, 0, 0x0002000D, jobList, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
            var startup = new StartupEx { Info = new() { Size = Marshal.SizeOf<StartupEx>() }, Attributes = attributes };
            var command = new StringBuilder(Quote(executable));
            foreach (var argument in arguments) command.Append(' ').Append(Quote(argument));
            if (script is not null) { executable = script.Interpreter; command = new(script.Command); environment = Marshal.StringToHGlobalUni(script.EnvironmentBlock); }
            if (highDpi)
            {
                if (environment != IntPtr.Zero) { Marshal.FreeHGlobal(environment); environment = IntPtr.Zero; }
                var entries = script is null ? Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().Select(x => x.Key + "=" + x.Value)
                    : script.EnvironmentBlock.Split('\0', StringSplitOptions.RemoveEmptyEntries).AsEnumerable();
                var block = string.Join('\0', entries.Where(x => !x.StartsWith("__COMPAT_LAYER=", StringComparison.OrdinalIgnoreCase)).Append("__COMPAT_LAYER=HIGHDPIAWARE").Order(StringComparer.OrdinalIgnoreCase)) + "\0\0";
                environment = Marshal.StringToHGlobalUni(block);
            }
            if (companionEnvironment is not null)
            {
                var values = (script is null ? Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().Select(x => x.Key + "=" + x.Value)
                    : script.EnvironmentBlock.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    .Where(x => !companionEnvironment.Keys.Any(key => x.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))).ToList();
                // Preserve the existing DPI intent while adding only companion-specific child variables.
                if (highDpi) { values.RemoveAll(x => x.StartsWith("__COMPAT_LAYER=", StringComparison.OrdinalIgnoreCase)); values.Add("__COMPAT_LAYER=HIGHDPIAWARE"); }
                values.AddRange(companionEnvironment.Select(x => x.Key + "=" + x.Value));
                if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
                environment = Marshal.StringToHGlobalUni(string.Join('\0', values.Order(StringComparer.OrdinalIgnoreCase)) + "\0\0");
            }
            if (command.Length >= 32767) throw new LaunchValidationException(LaunchOutcome.LaunchFailed, UiText.Get("启动参数总长度超过 Windows 限制，未执行程序。"));
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x00080000 | (environment == IntPtr.Zero ? 0u : 0x400u) | (beforeResume is null ? 0u : 4u), environment, cwd, ref startup, out var result)) throw new Win32Exception();
            // All ownership transfers immediately after OS creation; no fallible image query here.
            using var thread = new SafeFileHandle(result.Thread, true);
            var handle = new SafeProcessHandle(result.Process, true);
            transferred = true;
            if (beforeResume is not null)
            {
                try { if (GetProcessTimes(handle, out var creation, out _, out _, out _)) beforeResume(result.Pid, creation); }
                catch (Exception) { /* The already-created game must resume even if the companion fails. */ }
                finally { _ = ResumeThread(thread); }
            }
            return (handle, result.Pid, new(job));
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (jobList != IntPtr.Zero) Marshal.FreeHGlobal(jobList);
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
            if (!transferred) job.Dispose();
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    // Windows CRT argument encoding, never shell syntax. Tested through the real fixture argv.
    internal static string Quote(string value)
    {
        var output = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            output.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); output.Append(c); slashes = 0;
        }
        return output.Append('\\', slashes * 2).Append('"').ToString();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Accounting
    { public long User, Kernel, PeriodUser, PeriodKernel; public uint Faults, Total, Active, Terminated; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Startup
    {
        public int Size; public IntPtr Reserved, Desktop, Title;
        public uint X, Y, Width, Height, XChars, YChars, Fill, Flags;
        public ushort Show, ReservedSize; public IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupEx { public Startup Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public int Pid, Tid; }
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out Accounting info, uint size, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, IntPtr info, uint size, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DuplicateHandle(IntPtr source, SafeFileHandle handle, IntPtr target, out SafeFileHandle copy, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string cwd, ref StartupEx startup, out ProcessInfo info);
}
