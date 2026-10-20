using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;

namespace TheOrder.WinUI;
public sealed partial class MainWindow
{
    private readonly LaunchFileDiscovery launchFiles;
    private readonly IOfflineFilesClient launchFilesClient;
    private bool activatingLaunch;
    private async Task ActivateLaunchAsync()
    {
        if (activatingLaunch || commandsBlocked || !Launch.CanActivate || Launch.GameId is not { } game) return;
        activatingLaunch = true;
        try
        {
            if (!Launch.CanLaunch && !Launch.CanConfirm)
            {
                if (!ViewModel.CanEdit || !await ResolveUnsavedAsync()) return;
                var view = await launchFilesClient.InstallationsAsync(game, CancellationToken.None);
                var selected = view.Rows.FirstOrDefault(r => r.Installation.Id == Launch.SelectedInstallationId);
                if (selected is null)
                {
                    selected = view.Rows.Length == 1 ? view.Rows[0] : await ChooseLaunchItemAsync(view.Rows.ToArray(), r => r.Installation.Folder, UiText.Get("从哪个游戏文件夹启动？"));
                    if (selected is null) return;
                }
                var installation = selected.Installation;
                var files = await launchFiles.ReadAsync(installation.Folder);
                string? path;
                if (files.AutomaticChoice is { } automatic) path = automatic.Path;
                else
                {
                    var choice = await ChooseLaunchItemAsync(files.Choices.ToArray(), c => c.Label, UiText.Get("选择启动文件"), files.Message, installation.Folder);
                    path = choice?.Path ?? browsedLaunchFile;
                    browsedLaunchFile = null;
                    if (path is null) return;
                }
                if (Launch.GameId != game || ViewModel.SelectedId != game || commandsBlocked) return;
                var input = new InstallationInput(installation.Id, installation.Folder, path, installation.WorkingDirectory, installation.Arguments, view.Details.Preferred is null || view.Details.Preferred == installation.Id)
                    { SaveFolder = installation.SaveFolder, SteamAppId = installation.SteamAppId, Compatibility = installation.Compatibility };
                var saved = await ViewModel.ExecuteFileMutationAsync(token => launchFilesClient.SaveAsync(view.Details, input, token), game, preserveSelection: true);
                if (!saved.IsCommitted) return;
                await Launch.OpenAsync(game);
                await Launch.SelectAsync(installation.Id);
            }
            if (Launch.GameId != game || commandsBlocked) return;
            if (Launch.PositionChanged)
            {
                var confirm = new ContentDialog { XamlRoot = Root.XamlRoot, Title = UiText.Get("启动位置已变化"), Content = new TextBlock { Text = Launch.Position, TextWrapping = TextWrapping.Wrap }, PrimaryButtonText = UiText.Get("确认启动位置"), CloseButtonText = UiText.Get("取消") };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            }
            if (Launch.CanConfirm) await Launch.ConfirmAsync();
            if (Launch.GameId == game && !commandsBlocked && Launch.CanLaunch) await Launch.LaunchAsync();
        }
        catch (Exception) { ViewModel.ReportUnexpected(); }
        finally { activatingLaunch = false; }
    }
    private string? browsedLaunchFile;
    private async Task<T?> ChooseLaunchItemAsync<T>(T[] items, Func<T, string> label, string title, string message = "", string? folder = null) where T : class
    {
        var list = new ListView { MaxHeight = 340, SelectionMode = ListViewSelectionMode.Single };
        foreach (var item in items) list.Items.Add(new TextBlock { Text = label(item), TextWrapping = TextWrapping.Wrap, Tag = item });
        var content = new StackPanel { Spacing = 12 };
        if (folder is not null) content.Children.Add(new TextBlock { Text = folder, TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        if (message.Length > 0) content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(list);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, Content = content, PrimaryButtonText = UiText.Get(folder is null ? "下一步" : "选择并启动"), SecondaryButtonText = folder is null ? "" : UiText.Get("浏览本目录"), CloseButtonText = UiText.Get("取消"), IsPrimaryButtonEnabled = false };
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary && folder is not null)
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id) { SuggestedFolder = folder };
            picker.FileTypeFilter.Add(".exe"); picker.FileTypeFilter.Add(".cmd");
            browsedLaunchFile = (await picker.PickSingleFileAsync())?.Path;
        }
        return result == ContentDialogResult.Primary && list.SelectedItem is TextBlock { Tag: T chosen } ? chosen : null;
    }
}
