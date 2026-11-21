using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    // The existing diagnostic controller admits this only in an explicit Test profile.
    // A non-executable marker is reviewed, never launched or passed to a shell.
    private async Task<object> VerifyLaunchConfirmationAsync()
    {
        if (Program.Profile is not { Manifest.ProfileKind: NativeProfileKind.Test } profile)
            throw new InvalidOperationException("Isolated Test required");
        var game = extras.Snapshot?.Entries.FirstOrDefault(e => !e.Installations.IsEmpty)
            ?? throw new InvalidOperationException("Synthetic installation required");
        var folder = Path.GetFullPath(game.Installations[0].Folder);
        if (!InstallationPreflightService.Within(folder, Path.GetFullPath(profile.Root)))
            throw new InvalidOperationException("Only Test-owned installation folders are allowed");
        var executable = Path.Combine(folder, "the-order-confirmation-marker.exe");
        if (File.Exists(executable)) throw new InvalidOperationException("Test marker already exists");
        await File.WriteAllTextAsync(executable, "Non-executable launch-confirmation fixture. Never execute.");
        await Navigate(WorkspacePage.Details, game.Id); await Launch.OpenAsync(game.Id);
        await OpenLaunchSettingsAsync(game.Id);
        files.Draft!.Executable = executable;
        invoke(SaveButton()); await files.SaveAsync(); await Until(() => !files.HasDraft);
        await Navigate(WorkspacePage.Details, game.Id); await Until(() => Launch.CanConfirm && !Launch.Message.StartsWith("正在核对", StringComparison.Ordinal));
        await ConfirmLaunchPositionAsync();
        await Until(() => Launch.CanLaunch);

        await OpenLaunchSettingsAsync(game.Id);
        files.Draft!.MuteMode = (int)LaunchOverride.Enabled;
        invoke(SaveButton()); await files.SaveAsync(); await Until(() => !files.HasDraft);
        await Navigate(WorkspacePage.Details, game.Id); await Until(() => Launch.CanConfirm && !Launch.Message.StartsWith("正在核对", StringComparison.Ordinal)); await Task.Delay(100);
        var confirm = P1Elements(detailsPage).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "ConfirmLaunchPosition");
        if (!confirm.IsEnabled || confirm.Visibility != Microsoft.UI.Xaml.Visibility.Visible)
            throw new InvalidOperationException("Saved settings did not expose fresh position review");
        invoke(confirm); await Until(() => Launch.CanLaunch);
        var positions = await new IsolatedLaunchPositionStore(profile.Configuration()).ReadAsync(default);
        var saved = positions.Single(p => p.GameId == game.Id && p.InstallationId == game.Installations[0].Id);
        if (saved.Compatibility?.MuteInBackground != true || Launch.Runtime.ProcessCreated)
            throw new InvalidOperationException("Confirmation did not persist the reviewed settings without starting a process");
        return new { savedFileSettings = true, refreshedBeforeConfirm = true, confirmedNativeButton = true,
            persisted = true, noProcessCreated = true };

        Button SaveButton() => P1Elements(installationsPage).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "SaveInstallation");
        static void invoke(Button button)
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) ?? new ButtonAutomationPeer(button);
            if (!button.IsEnabled || peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider action)
                throw new InvalidOperationException("Test button is not available");
            action.Invoke();
        }
        async Task Until(Func<bool> done, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(done))] string condition = "")
        {
            for (var i = 0; i < 100; i++) { if (done()) return; await Task.Delay(50); }
            throw new InvalidOperationException($"Launch confirmation Test timed out: {condition}; {Launch.Message}; {Launch.Availability}; busy={Launch.Busy}; confirm={Launch.CanConfirm}; launch={Launch.CanLaunch}");
        }
    }
}
