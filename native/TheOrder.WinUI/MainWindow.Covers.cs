using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.WinUI.Views;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private CoverSelectionDialog? coverDialog;

    private async Task ChangePersonalBannerAsync(IPersonalBannerStore store, bool restore)
    {
        if (commandsBlocked || !ViewModel.CanEdit || !await ResolveUnsavedAsync() || ViewModel.Draft?.Source is not { } details) return;
        var path = restore ? null : await PickFile(".png", ".jpg", ".jpeg", ".webp");
        if (!restore && path is null) return;
        if (commandsBlocked || !ViewModel.CanEdit || ViewModel.Draft?.Source.Id != details.Id) return;
        await ViewModel.ExecuteFileMutationAsync(cancellation => PersonalBannerClient.SaveAsync(store, details.Id, path, cancellation), details.Id);
        if (ViewModel.PersistenceResult?.IsCommitted == true) detailsPage.RefreshPersonalBanner(details.Id);
    }

    private async Task ChooseWorkCoverAsync(OnlineCoverSelectionClient client, IRemoteCoverRegistry registry)
    {
        if (commandsBlocked || !await ResolveUnsavedAsync() || ViewModel.Draft?.Source is not { } details) return;
        // Close/cancel drains the whole operation, including network, dialog and transaction.
        await ViewModel.ExecuteFileMutationAsync(async cancellation =>
        {
            try
            {
                var options = await client.LoadAsync(details, cancellation);
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, options.Auth.Changed);
                stop.Token.ThrowIfCancellationRequested();
                if (commandsBlocked) return new(ClientOutcome.Cancelled, UiText.Get("封面选择已取消。"));
                var dialog = new CoverSelectionDialog(options, images, registry) { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme };
                coverDialog = dialog;
                using var cancel = stop.Token.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
                if (await dialog.ShowInShellAsync() != ContentDialogResult.Primary) return new(ClientOutcome.Cancelled, UiText.Get("封面选择已取消，原封面保留。"));
                stop.Token.ThrowIfCancellationRequested();
                return await client.SaveAsync(options, dialog.SelectedCoverId, stop.Token);
            }
            catch (OnlineException e) { return new(ClientOutcome.ValidationFailure, e.Message); }
            finally { coverDialog = null; }
        }, details.Id);
    }
}
