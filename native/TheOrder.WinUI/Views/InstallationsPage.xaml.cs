using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using TheOrder.Application;
using TheOrder.Presentation;
using TheOrder.Platform.Windows;

namespace TheOrder.WinUI.Views;
public sealed partial class InstallationsPage : Page
{
    public FilesViewModel ViewModel { get; }
    public WorkspaceViewModel Workspace { get; }
    public Func<Task<string?>>? ChooseFolder { get; set; }
    public Func<Task<string?>>? ChooseExecutable { get; set; }
    public WindowsLocalResources? LocalResources { get; set; }
    public Visibility ShowSteam(string? id) => id is null ? Visibility.Collapsed : Visibility.Visible;
    private InstallationDraft? displayedDraft;
    private InstallationDraft? observedDraft;
    private bool findingSave;
    public InstallationsPage(FilesViewModel viewModel, WorkspaceViewModel workspace)
    {
        ViewModel = viewModel; Workspace = workspace; InitializeComponent();
        Loaded += OnLoaded; Unloaded += OnUnloaded;
    }
    public Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowText(string? value) => Show(!string.IsNullOrWhiteSpace(value));
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnStateChanged; Workspace.PropertyChanged += OnStateChanged;
        ApplyResponsiveLayout(ActualWidth);
        RefreshActions();
    }
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout(e.NewSize.Width);
    private void ApplyResponsiveLayout(double width)
    {
        if (FolderInputGrid is null) return;
        var narrow = width is > 0 and < 640;
        ReadOnlySpan<(Grid Grid, Button Button)> pairs =
        [
            (FolderInputGrid, FolderBrowseButton),
            (ExecutableInputGrid, ExecutableBrowseButton),
            (SaveFolderInputGrid, SaveFolderBrowseButton),
            (WorkingDirectoryInputGrid, WorkingDirectoryBrowseButton)
        ];
        foreach (var (grid, button) in pairs)
        {
            grid.ColumnSpacing = narrow ? 0 : 8;
            grid.RowSpacing = narrow ? 6 : 0;
            grid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : GridLength.Auto;
            Grid.SetRow(button, narrow ? 1 : 0);
            Grid.SetColumn(button, narrow ? 0 : 1);
            button.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            button.VerticalAlignment = narrow ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        }
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnStateChanged; Workspace.PropertyChanged -= OnStateChanged;
        if (observedDraft is not null) observedDraft.PropertyChanged -= OnStateChanged;
        observedDraft = null;
    }
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, ViewModel.Draft) && e.PropertyName == nameof(InstallationDraft.Folder))
        { SaveCandidates.Visibility = SaveChoices.Visibility = Visibility.Collapsed; SaveChoices.Children.Clear(); }
        RefreshActions();
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SaveNotice is not null) SaveNotice.Visibility = Visibility.Collapsed;
        RefreshActions();
    }
    private void RefreshActions()
    {
        if (AddInstallationButton is null) return;
        if (IsLoaded && !ReferenceEquals(observedDraft, ViewModel.Draft))
        {
            if (observedDraft is not null) observedDraft.PropertyChanged -= OnStateChanged;
            observedDraft = ViewModel.Draft;
            if (observedDraft is not null) observedDraft.PropertyChanged += OnStateChanged;
        }
        var rows = ViewModel.Installations?.Rows;
        if (rows is { IsEmpty: false } && (Selected is null || !rows.Value.Contains(Selected)))
            InstallationList.SelectedItem = rows.Value.FirstOrDefault(r => r.Installation.Id == ViewModel.Draft?.Id)
                ?? rows.Value.FirstOrDefault(r => r.Preferred) ?? rows.Value[0];
        var editing = ViewModel.HasDraft;
        bool canChoose = Workspace.CanEdit && ViewModel.Installations is not null && !editing;
        InstallationList.IsEnabled = !editing;
        AddInstallationButton.IsEnabled = canChoose;
        EditInstallationButton.IsEnabled = canChoose && Selected is not null;
        PreferInstallationButton.IsEnabled = canChoose && Selected is { Preferred: false };
        RemoveInstallationButton.IsEnabled = canChoose && Selected is not null;
        OpenGameFolderButton.IsEnabled = canChoose && Selected is not null && LocalResources is not null;
        OpenSaveFolderButton.IsEnabled = canChoose && Selected?.Installation.SaveFolder is not null && LocalResources is not null;
        ToolTipService.SetToolTip(OpenSaveFolderButton, Selected?.Installation.SaveFolder is null
            ? "先在“修改路径与启动设置”中设置存档文件夹" : "在文件资源管理器中打开已保存的存档位置");
        EmptyInstallations.Visibility = Show(rows is { IsEmpty: true });
        FindSaveFolderButton.IsEnabled = Workspace.CanEdit && !findingSave && !string.IsNullOrWhiteSpace(ViewModel.Draft?.Folder);
        SaveInstallationButton.IsEnabled = Workspace.CanEdit && !string.IsNullOrWhiteSpace(ViewModel.Draft?.Folder);
        if (!ReferenceEquals(displayedDraft, ViewModel.Draft))
        {
            displayedDraft = ViewModel.Draft;
            SaveNotice.Visibility = SaveCandidates.Visibility = SaveChoices.Visibility = Visibility.Collapsed;
            SaveChoices.Children.Clear();
            SaveFolderSection.IsExpanded = LaunchHelpersSection.IsExpanded = AdvancedLaunchSection.IsExpanded = false;
            DraftHeading.Text = displayedDraft is not null && rows?.Any(r => r.Installation.Id == displayedDraft.Id) == true
                ? "修改路径与启动设置" : "关联已有游戏文件夹";
        }
        ApplyResponsiveLayout(ActualWidth);
    }
    internal void ExpandSectionForEvidence(string id)
    {
        if (id == "SaveFolderPath") SaveFolderSection.IsExpanded = true;
        if (id == "LaunchHelpers") LaunchHelpersSection.IsExpanded = true;
        ApplyResponsiveLayout(ActualWidth);
        UpdateLayout();
    }
    internal async Task ShowLaunchHelpersAsync()
    {
        for (int i = 0; i < 20 && !IsLoaded; i++) await Task.Delay(25);
        if (!IsLoaded || ViewModel.Draft is null) return;
        RefreshActions();
        LaunchHelpersSection.IsExpanded = !ViewModel.Draft.UsesGlobal;
        for (int i = 0; i < 20 && LaunchHelpersSection.ActualHeight == 0; i++) await Task.Delay(25);
        if (!IsLoaded || ViewModel.Draft is null) return;
        InstallationList.SelectedItem = ViewModel.Installations?.Rows.FirstOrDefault(r => r.Installation.Id == ViewModel.Draft.Id);
        LaunchHelpersSection.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = InteractionMotion.Enabled });
    }
    private InstallationRow? Selected => InstallationList.SelectedItem as InstallationRow;
    private void ShowEditor()
    { UpdateLayout(); DraftHeading.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = InteractionMotion.Enabled }); }
    private void OnAdd(object sender, RoutedEventArgs e) { ViewModel.Edit(null); ShowEditor(); }
    private void OnEdit(object sender, RoutedEventArgs e) { if (Selected is { } row) { ViewModel.Edit(row); ShowEditor(); } }
    private void OnCancel(object sender, RoutedEventArgs e) => ViewModel.CancelDraft();
    private async void OnFolder(object sender, RoutedEventArgs e) => await Pick(ChooseFolder, (draft, value) => draft.Folder = value);
    private async void OnExecutable(object sender, RoutedEventArgs e) => await Pick(ChooseExecutable, (draft, value) => draft.Executable = value);
    private async void OnWorking(object sender, RoutedEventArgs e) => await Pick(ChooseFolder, (draft, value) => draft.WorkingDirectory = value);
    private async void OnSaveFolder(object sender, RoutedEventArgs e) => await Pick(ChooseFolder, (draft, value) => draft.SaveFolder = value);
    private void OnResetLaunchOverrides(object sender, RoutedEventArgs e) => ViewModel.Draft?.ResetAuxiliaries();
    private async void OnOpenGameFolder(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row || LocalResources is null) return;
        try { await LocalResources.OpenFolderAsync(row.Installation.Folder); SaveNotice.Visibility = Visibility.Collapsed; }
        catch { SaveNotice.Text = "游戏文件夹无法打开。若已移动游戏，请选择“修改路径与启动设置”重新定位。"; SaveNotice.Visibility = Visibility.Visible; }
    }
    private async void OnOpenSave(object sender, RoutedEventArgs e)
    { try { if (Selected?.Installation.SaveFolder is not { } path) throw new InvalidDataException("请先在“修改路径与启动设置”中选择存档文件夹。"); if (LocalResources is not null) await LocalResources.OpenFolderAsync(path); SaveNotice.Visibility = Visibility.Collapsed; } catch (Exception error) { SaveNotice.Text = error is InvalidDataException ? error.Message : "存档文件夹无法打开，请在“修改路径与启动设置”中重新选择。"; SaveNotice.Visibility = Visibility.Visible; } }
    private async void OnFindSave(object sender, RoutedEventArgs e)
    {
        if (LocalResources is null || ViewModel.Draft is not { } draft || findingSave) return;
        var searchedFolder = draft.Folder;
        findingSave = true; RefreshActions();
        try
        {
            var paths = await LocalResources.FindSaveFoldersAsync(searchedFolder);
            if (!ReferenceEquals(draft, ViewModel.Draft) || draft.Folder != searchedFolder) return;
            SaveChoices.Children.Clear(); SaveChoices.Visibility = Show(paths.Length > 1);
            SaveCandidates.Text = paths.IsEmpty ? "游戏目录中未找到常见存档文件夹。可手动选择文档、AppData 等位置。"
                : paths.Length == 1 ? "已填入找到的位置，请确认后点击“保存设置”。" : "找到以下可能的存档位置，请选择正确的一项，再保存设置。";
            SaveCandidates.Visibility = Visibility.Visible;
            if (paths.Length == 1) { draft.SaveFolder = paths[0]; Bindings.Update(); }
            else foreach (var path in paths)
            {
                var choice = new Button { Content = new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap }, HorizontalContentAlignment = HorizontalAlignment.Left };
                choice.Click += (_, _) => { if (ReferenceEquals(draft, ViewModel.Draft) && draft.Folder == searchedFolder) { draft.SaveFolder = path; Bindings.Update(); SaveCandidates.Text = "已填入所选位置，请点击“保存设置”。"; } };
                SaveChoices.Children.Add(choice);
            }
        }
        catch { if (ReferenceEquals(draft, ViewModel.Draft) && draft.Folder == searchedFolder) { SaveCandidates.Text = "查找未完成，请手动选择存档文件夹。"; SaveCandidates.Visibility = Visibility.Visible; } }
        finally { findingSave = false; RefreshActions(); }
    }
    private async Task Pick(Func<Task<string?>>? picker, Action<InstallationDraft, string> apply)
    { try { var draft = ViewModel.Draft; if (draft is not null && picker is not null && await picker() is { } value && ReferenceEquals(draft, ViewModel.Draft)) { apply(draft, value); Bindings.Update(); RefreshActions(); } } catch (Exception) { Workspace.ReportUnexpected(); } }
    private void OnArgument(object sender, RoutedEventArgs e) { if (ViewModel.Draft is { } draft && draft.Arguments.Count < 100) draft.Arguments.Add(new("")); }
    private void OnRemoveArgument(object sender, RoutedEventArgs e) { if (sender is Button { Tag: ArgumentDraft arg }) ViewModel.Draft?.Arguments.Remove(arg); }
    private async void OnSave(object sender, RoutedEventArgs e) { try { await ViewModel.SaveAsync(); } catch (Exception) { Workspace.ReportUnexpected(); } }
    private async void OnPrefer(object sender, RoutedEventArgs e) { try { if (Selected is { } row) await ViewModel.PreferAsync(row); } catch (Exception) { Workspace.ReportUnexpected(); } }
    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Selected is not { } row) return;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "解除此游戏文件夹的关联？", Content = "游戏文件和书架中的作品、游玩记录都会保留。解除后，这部作品不再从以下文件夹启动：\n\n" + row.Installation.Folder, PrimaryButtonText = "解除关联", CloseButtonText = "保留关联", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowInShellAsync() == ContentDialogResult.Primary) await ViewModel.RemoveAsync(row);
        }
        catch (Exception) { Workspace.ReportUnexpected(); }
    }
}
