using System.ComponentModel;
using Microsoft.UI.Xaml;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

public sealed partial class DetailsPage
{
    private NextMoeSettingsViewModel? personalSync;
    private CancellationTokenSource? personalFactsRead;
    private (string Work, string Account, long Generation)? displayedCloudFacts;
    internal void ConfigurePersonalSync(NextMoeSettingsViewModel sync)
    {
        personalSync = sync;
        Loaded += (_, _) => { sync.PropertyChanged += OnPersonalSync; _ = RefreshPersonalFactsAsync(); };
        Unloaded += (_, _) => { sync.PropertyChanged -= OnPersonalSync; personalFactsRead?.Cancel(); ShowCloudFacts(null, ""); };
    }
    private void OnPersonalSync(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(NextMoeSettingsViewModel.Status) or nameof(NextMoeSettingsViewModel.LastSync)) _ = RefreshPersonalFactsAsync(); }
    private async Task RefreshPersonalFactsAsync()
    {
        personalFactsRead?.Cancel();
        if (!IsLoaded || personalSync is not { Status: { Mode: NextMoeAccessMode.Account, Account: { } account } } sync
            || ViewModel.Draft?.Source.Metadata is not { } work)
        {
            ShowCloudFacts(null, "");
            return;
        }
        var key = (work.Id, account.Id, sync.Access.AuthContext.Generation);
        if (displayedCloudFacts != key) ShowCloudFacts(null, "");
        using var stop = new CancellationTokenSource(); personalFactsRead = stop;
        bool Current() => !stop.IsCancellationRequested && IsLoaded && ViewModel.Draft?.Source.Metadata?.Id == work.Id
            && sync.Status is { Mode: NextMoeAccessMode.Account, Account.Id: var id } && id == account.Id
            && sync.Access.AuthContext.Generation == key.Generation;
        try
        {
            var facts = await sync.ImportedFactsAsync(work.Id, stop.Token);
            if (!Current()) return;
            if (facts is null) { ShowCloudFacts(null, ""); return; }
            var values = new List<string>();
            if (facts.Minutes is { } minutes) values.Add($"云端累计时长：{minutes / 60} 小时 {minutes % 60} 分钟");
            if (facts.Rating is { } rating) values.Add($"NextMoe 个人评分：{rating} / 10");
            if (facts.State is { } status) values.Add("云端状态：" + StatusNames.Display(PersonalSyncPlanning.LocalStatus(status))
                + (facts.Completion switch { "one_route" => " · 已完成一条线路", "main" => " · 已完成主线", "all" => " · 全部完成", _ => "" }));
            ShowCloudFacts(key, string.Join("\n", values));
        }
        catch (OperationCanceledException) { }
        catch { if (Current()) ShowCloudFacts(key, "云端个人记录读取未完成，请在设置中重试。"); }
        finally { if (ReferenceEquals(personalFactsRead, stop)) personalFactsRead = null; }
    }

    private void ShowCloudFacts((string Work, string Account, long Generation)? key, string text)
    {
        if (displayedCloudFacts == key && CloudPersonalText.Text == text) return;
        displayedCloudFacts = key;
        CloudPersonalText.Text = text;
        CloudPersonalSection.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (text.Length > 0) InteractionMotion.RevealContent(CloudPersonalSection, .74f);
    }
}
