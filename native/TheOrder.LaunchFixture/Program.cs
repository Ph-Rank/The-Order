using System.Diagnostics;
using System.Text.Json;

// Project-owned deterministic fixture. Never copied into a real game directory.
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Test-only broker adapters mirror LE's joined arguments and Steam's structured
        // forwarding. Targets are restricted to this project's marked synthetic fixture.
        var imageName = Path.GetFileName(Environment.ProcessPath);
        if (imageName?.Equals("Magpie.exe", StringComparison.OrdinalIgnoreCase) == true && args.Length == 0)
        {
            var helperReports = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "observations"));
            if (!File.Exists(Path.Combine(helperReports, ".the-order-launch-fixture"))) return;
            var helperName = "magpie-" + Environment.ProcessId;
            File.WriteAllText(Path.Combine(helperReports, helperName + ".json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, mode = "scaling-helper" }));
            var helperDeadline = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(helperReports, helperName + ".stop")) && helperDeadline.Elapsed < TimeSpan.FromSeconds(90)) Thread.Sleep(50);
            return;
        }
        if (imageName?.Equals("LEProc.exe", StringComparison.OrdinalIgnoreCase) == true && args.Length >= 4)
        {
            var reports = args[1].Trim('"'); var target = Path.Combine(AppContext.BaseDirectory, "simple_game.exe");
            if (Path.GetFullPath(args[0]) != target || !File.Exists(Path.Combine(reports, ".the-order-launch-fixture"))) return;
            using var child = Process.Start(new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory, Arguments = string.Join(' ', args.Skip(1)) });
            return;
        }
        if (imageName?.Equals("Steam.exe", StringComparison.OrdinalIgnoreCase) == true && args.Length >= 5 && args[0] == "-applaunch" && args[1] == "480")
        {
            if (!File.Exists(Path.Combine(args[2], ".the-order-launch-fixture"))) return;
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "simple_game.exe")) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            foreach (var arg in args.Skip(2)) start.ArgumentList.Add(arg);
            using var child = Process.Start(start); return;
        }
        if (args.Length < 3) return;
        var root = Path.GetFullPath(args[0]); var mode = args[1]; var name = args[2];
        if (!File.Exists(Path.Combine(root, ".the-order-launch-fixture"))) return;
        // Keep this synthetic process tree deterministic on desktops with legacy
        // global UI hooks/AppInit extensions. This is process-local, not a machine
        // setting or a production launch policy. Read back rather than assume success.
        uint disableExtensionPoints = 1;
        if (!SetProcessMitigationPolicy(6, ref disableExtensionPoints, 4)
            || !GetProcessMitigationPolicy(GetCurrentProcess(), 6, out var applied, 4) || applied != 1)
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Synthetic fixture extension-point isolation failed.");
        var stopFile = Path.Combine(root, name + ".stop");
        GetStartupInfo(out var startup);
        File.WriteAllText(Path.Combine(root, name + ".json"), JsonSerializer.Serialize(new
        { pid = Environment.ProcessId, cwd = Environment.CurrentDirectory, arguments = args.Skip(3).ToArray(), commandLine = System.Runtime.InteropServices.Marshal.PtrToStringUni(GetCommandLine()), startupFlags = startup.Flags, mode, started = DateTimeOffset.UtcNow, extensionPointsDisabled = applied == 1, compatibilityLayer = Environment.GetEnvironmentVariable("__COMPAT_LAYER") }));
        if (mode == "empty") return;
        if (mode == "gap-helper") { Spawn("child_game.exe", "game", name + "-game"); return; }
        if (mode is "launcher" or "chain" or "helper" or "multiple" or "quick-launcher")
        {
            bool observedHandoff = mode == "launcher" && args.Skip(3).Contains("--observed-handoff");
            if (observedHandoff)
            {
                if (!WaitForObservation("parent")) return;
            }
            else Thread.Sleep(600);
            if (mode == "chain") Spawn("helper.exe", "helper", name + "-helper");
            else
            {
                Spawn("child_game.exe", mode == "quick-launcher" ? "quick" : "game", name + "-game");
                if (mode == "multiple") Spawn("second_game.exe", "game", name + "-other");
            }
            // The confirmed-handoff tests control this scenario's premise: the
            // real observer must bind the live child before the parent exits.
            // Other fixture modes still exercise the original bounded/limited paths.
            if (observedHandoff) WaitForObservation("child");
            else Thread.Sleep(700);
            return;
        }
        System.Windows.Forms.Application.EnableVisualStyles();
        using var form = new Form { Text = "The Order · synthetic process " + name, Width = 420, Height = 220 };
        form.Controls.Add(new Label { Text = "Synthetic launch fixture\nPID: " + Environment.ProcessId, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        bool spawned = false;
        timer.Tick += (_, _) =>
        {
            if (mode == "delayed-gap" && !spawned && File.Exists(Path.Combine(root, name + ".spawn")))
            { spawned = true; Spawn("helper.exe", "gap-helper", name + "-helper"); }
            if (mode == "quick" || File.Exists(stopFile)) form.Close();
        };
        timer.Start();
        System.Windows.Forms.Application.Run(form);
        return;
        void Spawn(string executable, string childMode, string childName)
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, executable)) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
            start.ArgumentList.Add(root); start.ArgumentList.Add(childMode); start.ArgumentList.Add(childName);
            using var child = Process.Start(start);
        }
        bool WaitForObservation(string stage)
        {
            var ready = Path.Combine(root, name + "." + stage + "-observed");
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                if (File.Exists(stopFile) || deadline.Elapsed >= TimeSpan.FromSeconds(15)) return false;
                Thread.Sleep(10); // Poll a concrete test-protocol condition, not a launch delay.
            }
            return true;
        }
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetCommandLineW")]
    private static extern IntPtr GetCommandLine();
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct Startup
    {
        public int Size; public IntPtr Reserved, Desktop, Title;
        public uint X, Y, Width, Height, XChars, YChars, Fill, Flags;
        public ushort Show, ReservedSize; public IntPtr ReservedBytes, Input, Output, Error;
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetStartupInfoW")]
    private static extern void GetStartupInfo(out Startup startup);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetProcessMitigationPolicy(int policy, ref uint flags, nuint size);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetProcessMitigationPolicy(IntPtr process, int policy, out uint flags, nuint size);
}
