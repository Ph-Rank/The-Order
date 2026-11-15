using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;
public sealed partial class ImportPage : Page
{
    public FilesViewModel ViewModel { get; }
    public WorkspaceViewModel Workspace { get; }
    public RecognitionViewModel Recognition { get; }
    public Func<Task<string?>>? ChooseFolder { get; set; }
    public Func<Task>? OpenLibrarySources { get; set; }
    public Func<Task>? OpenLibrary { get; set; }
    public Func<ImmutableArray<ScanCandidate>, Task>? IgnoreDiscovered { get; set; }
    public ImportPage(FilesViewModel viewModel, WorkspaceViewModel workspace, RecognitionViewModel recognition)
    {
        ViewModel = viewModel; Workspace = workspace; Recognition = recognition; InitializeComponent();
        SynchronizeActions();
        Loaded += (_, _) => { ViewModel.PropertyChanged += OnState; Workspace.PropertyChanged += OnState; SynchronizeActions(); };
        Unloaded += (_, _) => { ViewModel.PropertyChanged -= OnState; Workspace.PropertyChanged -= OnState; };
    }
    public Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TextVisible(string? text) => Visible(!string.IsNullOrWhiteSpace(text));
    private void OnState(object? sender, PropertyChangedEventArgs e)
    {
        SynchronizeActions();
        if (e.PropertyName == nameof(FilesViewModel.Candidates) && ViewModel.Stage == ImportStage.Review && ViewModel.HasCandidates)
            InteractionMotion.RevealContent(CandidateList);
    }
    private void SynchronizeActions()
    {
        if (CandidateList is null || ImportSelectedButton is null) return;
        var editable = Workspace.CanEdit;
        ImportSelectedButton.IsEnabled = editable && ViewModel.CanImportSelection;
        ImportSelectedButton.Content = ViewModel.SelectedImportCount > 0 ? UiText.Format($"加入书架（{ViewModel.SelectedImportCount}）") : UiText.Get("加入书架");
        SelectAllButton.IsEnabled = editable && ViewModel.CanSelectImportItems;
        ClearSelectionButton.IsEnabled = editable && ViewModel.SelectedImportCount > 0;
        IgnoreSelectedButton.IsEnabled = editable && ViewModel.IsSourceDiscovery && ViewModel.SelectedImportCount > 0;
        IgnoreAllButton.IsEnabled = editable && ViewModel.IsSourceDiscovery && ViewModel.HasCandidates;
        SelectionSummary.Text = ViewModel.SelectedImportCount == 0 ? UiText.Get("尚未选择游戏") : UiText.Format($"已选择 {ViewModel.SelectedImportCount} 个游戏");
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => ViewModel.SelectForImport(CandidateList.SelectedItems.OfType<ScanCandidate>());
    private async void OnChoose(object sender, RoutedEventArgs e)
    { try { if (ChooseFolder is not null && await ChooseFolder() is { } path) await ViewModel.ScanAsync(path); } catch (Exception) { Workspace.ReportUnexpected(); } }
    private async void OnLibrarySources(object sender, RoutedEventArgs e)
    { try { if (OpenLibrarySources is not null) await OpenLibrarySources(); } catch { Workspace.ReportUnexpected(); } }
    private async void OnLibrary(object sender, RoutedEventArgs e)
    { try { if (OpenLibrary is not null) await OpenLibrary(); } catch { Workspace.ReportUnexpected(); } }
    private void OnCancel(object sender, RoutedEventArgs e) => ViewModel.CancelScan();
    private async void OnRetry(object sender, RoutedEventArgs e)
    { try { await ViewModel.RetryScanAsync(); } catch (Exception) { Workspace.ReportUnexpected(); } }
    private void OnSelectAll(object sender, RoutedEventArgs e) { CandidateList.SelectedItems.Clear(); foreach (var item in ViewModel.Candidates.Where(c => c.Preflight.CanAttach)) CandidateList.SelectedItems.Add(item); }
    private void OnClearSelection(object sender, RoutedEventArgs e) => CandidateList.SelectedItems.Clear();
    private async void OnIgnoreSelected(object sender, RoutedEventArgs e) => await IgnoreAsync(ViewModel.SelectedImportCandidates);
    private async void OnIgnoreAll(object sender, RoutedEventArgs e) => await IgnoreAsync([.. ViewModel.Candidates]);
    private async Task IgnoreAsync(ImmutableArray<ScanCandidate> selected)
    {
        try { if (ViewModel.IsSourceDiscovery && !selected.IsEmpty && IgnoreDiscovered is not null) await IgnoreDiscovered(selected); }
        catch { Workspace.ReportUnexpected(); }
    }
    private async void OnImport(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = ViewModel.SelectedImportCandidates; if (!Workspace.CanEdit || !ViewModel.CanImportSelection || selected.IsEmpty) return;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = UiText.Format($"将 {selected.Length} 个游戏加入书架？"), Content = UiText.Get("加入后会自动查找封面和简介；需要你确认的结果会出现在“待核对”中。游戏文件留在原位置，重复导入会自动跳过。"), PrimaryButtonText = UiText.Get("加入书架"), CloseButtonText = UiText.Get("继续选择"), DefaultButton = ContentDialogButton.Primary };
            if (await dialog.ShowInShellAsync() == ContentDialogResult.Primary) await ViewModel.ImportAsync(selected);
        }
        catch (Exception) { Workspace.ReportUnexpected(); }
    }
}
