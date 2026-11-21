using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private async Task RunPersonalFixtureAsync(string? action)
    {
        if (Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test || Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PERSONAL_FIXTURE") != "1"
            || Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_DIAGNOSTICS") != "1" || Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_ONLINE_FIXTURE") != "1") throw new InvalidOperationException("Synthetic-only personal fixture required.");
        await Navigate(WorkspacePage.Settings);
        switch (action)
        {
            case "login": await nextMoeSettings.LoginAsync(); break;
            case "sync": await nextMoeSettings.AutomaticAsync(); break;
            case "preview": nextMoeSettings.TimeDirection = 2; nextMoeSettings.StateDirection = 3; nextMoeSettings.ReadRatings = true; await nextMoeSettings.PreviewAsync(); break;
            case "apply": await nextMoeSettings.ApplyAsync(); break;
            case "logout": await nextMoeSettings.LogoutAsync(); break;
            case "anonymous": await nextMoeSettings.SelectModeAsync(0); break;
            default: throw new InvalidOperationException("Unsupported personal fixture action.");
        }
    }
}
