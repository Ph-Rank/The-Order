using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TheOrder.Platform.Windows;

// Background companions must not claim that the foreground game is starting.
internal sealed class WindowsGuideHelperProcess(SafeProcessHandle handle) : IDisposable
{
    internal static WindowsGuideHelperProcess Start(string binary, IEnumerable<string> arguments)
    {
        var command = new StringBuilder(WindowsProcessLifetimeScope.Quote(binary));
        foreach (string argument in arguments) command.Append(' ').Append(WindowsProcessLifetimeScope.Quote(argument));
        const uint ForceOffFeedback = 0x80, UseShowWindow = 1, CreateNoWindow = 0x08000000;
        var startup = new Startup { Size = Marshal.SizeOf<Startup>(), Flags = ForceOffFeedback | UseShowWindow, Show = 0 };
        if (!CreateProcess(binary, command, IntPtr.Zero, IntPtr.Zero, false, CreateNoWindow, IntPtr.Zero,
                Path.GetDirectoryName(binary)!, ref startup, out var created)) throw new Win32Exception();
        CloseHandle(created.Thread);
        return new(new SafeProcessHandle(created.Process, true));
    }
    internal bool WaitForExit(int milliseconds = Timeout.Infinite)
    {
        uint result = WaitForSingleObject(handle, milliseconds == Timeout.Infinite ? uint.MaxValue : checked((uint)milliseconds));
        if (result == uint.MaxValue) throw new Win32Exception();
        return result == 0;
    }
    internal int ExitCode => GetExitCodeProcess(handle, out uint code) ? unchecked((int)code) : throw new Win32Exception();
    internal void Kill()
    {
        if (!TerminateProcess(handle, 1) && !WaitForExit(0)) throw new Win32Exception();
    }
    public void Dispose() => handle.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct Startup
    {
        public int Size; public IntPtr Reserved, Desktop, Title;
        public uint X, Y, Width, Height, XChars, YChars, Fill, Flags;
        public ushort Show, ReservedSize; public IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public int Pid, Tid; }
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string binary, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string cwd, ref Startup startup, out ProcessInfo created);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(SafeProcessHandle process, uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
