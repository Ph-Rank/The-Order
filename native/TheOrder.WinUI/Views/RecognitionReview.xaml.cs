using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;
namespace TheOrder.WinUI.Views;
public sealed partial class RecognitionReview : UserControl, IImageHost
{
    public RecognitionViewModel ViewModel { get; }
    public IImagePipeline Images { get; }
    public RecognitionReview(RecognitionViewModel viewModel, IImagePipeline images)
    {
        ViewModel = viewModel; Images = images; InitializeComponent(); RemoteRichFacts.Images = images; ManualIdBox.KeyDown += OnManualKey;
        CandidateList.AddHandler(PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnCandidateWheel), true);
        SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 460;
            Grid.SetRow(ManualApplyButton, narrow ? 1 : 0);
            Grid.SetColumn(ManualApplyButton, narrow ? 0 : 1);
        };
    }
    public Visibility ShowText(string? value) => !string.IsNullOrWhiteSpace(value) ? Visibility.Visible : Visibility.Collapsed;
    private void OnCandidateWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(CandidateList).Properties;
        if (point.IsHorizontalMouseWheel || point.MouseWheelDelta == 0) return;
        var inner = FindScroll(CandidateList);
        if (inner is null || point.MouseWheelDelta > 0 && inner.VerticalOffset > .5 ||
            point.MouseWheelDelta < 0 && inner.VerticalOffset < inner.ScrollableHeight - .5) return;
        for (DependencyObject? parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(this); parent is not null; parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer outer)
            {
                outer.ChangeView(null, Math.Clamp(outer.VerticalOffset - point.MouseWheelDelta * .4, 0, outer.ScrollableHeight), null, true);
                e.Handled = true; break;
            }
    }
    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScroll(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
    private Func<string, Task>? openLink;
    public void ConfigureCatalog(IRemoteCoverRegistry registry, Func<string, Task> openLink) { this.openLink = openLink; RemoteRichFacts.Registry = registry; RemoteRichFacts.OpenLink = openLink; }
    private async void OnManualKey(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e) { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; ViewModel.ManualId = ManualIdBox.Text; await ViewModel.OpenDetailsAsync(ViewModel.ManualId); } }
    private void OnCandidate(object sender, SelectionChangedEventArgs e) { if (e.AddedItems.FirstOrDefault() is RecognitionCandidateCard card) ViewModel.SelectCandidate(card.Id); }
    private async void OnRemoteDetails(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string id }) await ViewModel.OpenDetailsAsync(id); }
    private async void OnOpenLink(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton { Tag: string url } || !CandidateDetailLinks.Allowed(url)) return;
        try
        {
            if (openLink is not null) await openLink(url); else if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) ViewModel.ReportLinkFailure();
        }
        catch (Exception) { ViewModel.ReportLinkFailure(); }
    }
    private async void OnRerun(object sender, RoutedEventArgs e) => await ViewModel.RerunAsync();
    private async void OnIgnore(object sender, RoutedEventArgs e) => await ViewModel.IgnoreAsync();
    private async void OnApply(object sender, RoutedEventArgs e) => await ViewModel.ConfirmSelectionAsync();
    private async void OnManualCandidate(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        ViewModel.SelectCandidate(id);
        if (!ViewModel.CanLinkSelectedManually) return;
        var game = ViewModel.SelectedGameId; var review = ViewModel.SelectedId;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "手动关联所选作品？",
            Content = ViewModel.SelectedCandidateHasConflict ? "识别证据存在差异。请核对作品和版本；继续后按你的明确选择关联，个人名称、封面及笔记保留。" : "将这个目录作品关联到当前游戏，保留个人资料。",
            PrimaryButtonText = ViewModel.SelectedCandidateHasConflict ? "仍要关联" : "确认关联", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && ViewModel.SelectedGameId == game && ViewModel.SelectedId == review
            && ViewModel.SelectedCandidateItem?.Id == id && ViewModel.CanLinkSelectedManually) await ViewModel.ManualLinkSelectedAsync();
    }
    private async void OnManual(object sender, RoutedEventArgs e) => await ViewModel.ManualLinkAsync();
    private async void OnSavePending(object sender, RoutedEventArgs e) => await ViewModel.SaveManualPendingAsync();
    private async void OnOpenPending(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaUserLink link }) { ViewModel.ManualId = link.Target.Id; await ViewModel.OpenDetailsAsync(link.Target.Id); }
    }
    private async void OnRemovePending(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: MediaUserLink link }) await ViewModel.RemoveManualPendingAsync(link); }

}
