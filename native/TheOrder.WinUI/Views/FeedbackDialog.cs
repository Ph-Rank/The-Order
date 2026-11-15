using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace TheOrder.WinUI.Views;

internal sealed class FeedbackDialog : ContentDialog
{
    private const double DialogWidth = 560;
    private const double KindWidth = 270;
    private readonly GitHubFeedbackClient client;
    private readonly FeedbackEnvironment environment;
    private readonly string diagnostics;
    private readonly CancellationTokenSource closed = new();
    private readonly SelectedFirstComboBox kind;
    private readonly TextBox subject;
    private readonly TextBox details;
    private readonly CheckBox includeDiagnostics;
    private readonly StackPanel editor = new() { Spacing = 12 };
    private readonly StackPanel preview = new() { Spacing = 10, Visibility = Visibility.Collapsed };
    private readonly StackPanel authorization = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock previewTitle = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock previewBody = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock account = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock deviceCode = new() { FontSize = 22, IsTextSelectionEnabled = true };
    private readonly Button disconnect;
    private readonly Button copy;
    private readonly Button manual;
    private readonly InfoBar status = new() { IsClosable = false };
    private readonly ExternalLinkButton issueLink = new() { Visibility = Visibility.Collapsed };
    private readonly ScrollViewer scroll;
    private CancellationTokenSource? authorizationCancellation;
    private FeedbackReport? report;
    private string? login;
    private bool busy;
    private bool submitting;
    private bool exiting;
    private bool unknownSubmission;
    private bool published;

    internal FeedbackDialog(XamlRoot root, GitHubFeedbackClient client, FeedbackEnvironment environment, string diagnostics)
    {
        this.client = client;
        this.environment = environment;
        this.diagnostics = diagnostics;
        XamlRoot = root;
        Title = UiText.Get("提交反馈");
        DefaultButton = ContentDialogButton.None;
        PrimaryButtonStyle = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        Resources["ContentDialogMaxWidth"] = DialogWidth;
        AutomationProperties.SetAutomationId(this, "FeedbackDialog");
        kind = new SelectedFirstComboBox
        {
            Header = UiText.Get("反馈类型"),
            HorizontalAlignment = HorizontalAlignment.Left,
            OptionsSource = new[] { UiText.Get("问题反馈"), UiText.Get("功能建议") },
            OptionIndex = 0
        };
        AutomationProperties.SetAutomationId(kind, "FeedbackKind");
        subject = Field("标题", "FeedbackTitle", FeedbackReport.TitleLimit);
        details = Field("说明", "FeedbackDescription", FeedbackReport.DescriptionLimit);
        details.AcceptsReturn = true;
        details.TextWrapping = TextWrapping.Wrap;
        details.PlaceholderText = UiText.Get("发生了什么？怎样能重现？");
        kind.OptionChanged += (_, _) =>
        {
            details.PlaceholderText = UiText.Get(SelectedKind == FeedbackKind.Problem ? "发生了什么？怎样能重现？" : "你希望实现什么？");
        };
        includeDiagnostics = new CheckBox
        {
            Content = UiText.Get("附上本次运行的诊断摘要"),
            Visibility = string.IsNullOrEmpty(diagnostics) ? Visibility.Collapsed : Visibility.Visible
        };
        editor.Children.Add(kind);
        editor.Children.Add(subject);
        editor.Children.Add(details);
        editor.Children.Add(includeDiagnostics);
        subject.TextChanged += (_, _) => UpdateActions();
        details.TextChanged += (_, _) => UpdateActions();

        previewTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        AutomationProperties.SetAutomationId(previewTitle, "FeedbackPreviewTitle");
        AutomationProperties.SetAutomationId(previewBody, "FeedbackPreviewBody");
        var codeLink = new ExternalLinkButton
        {
            Content = UiText.Get("打开 GitHub 授权"), NavigateUri = GitHubFeedbackClient.VerificationPage
        };
        var cancelAuthorization = new Button { Content = UiText.Get("取消授权") };
        cancelAuthorization.Click += (_, _) => authorizationCancellation?.Cancel();
        var authorizationActions = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        authorizationActions.Children.Add(codeLink);
        authorizationActions.Children.Add(cancelAuthorization);
        AutomationProperties.SetAutomationId(deviceCode, "FeedbackDeviceCode");
        authorization.Children.Add(Label("在 GitHub 输入此代码。授权后回到这里确认提交。"));
        authorization.Children.Add(deviceCode);
        authorization.Children.Add(authorizationActions);

        copy = new Button { Content = UiText.Get("复制反馈") };
        copy.Click += (_, _) => CopyFeedback(includeTitle: true);
        disconnect = new Button { Content = UiText.Get("断开 GitHub") };
        disconnect.Click += async (_, _) => await RunAsync(async token =>
        {
            await client.DisconnectAsync(token);
            login = null;
        });
        manual = new Button { Content = UiText.Get("在浏览器中提交"), Visibility = Visibility.Collapsed };
        manual.Click += async (_, _) => await OpenManualAsync();
        var actions = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        actions.Children.Add(copy);
        actions.Children.Add(disconnect);
        actions.Children.Add(manual);
        preview.Children.Add(Label("提交后，以下内容将在 GitHub 公开显示。"));
        preview.Children.Add(previewTitle);
        preview.Children.Add(previewBody);
        preview.Children.Add(actions);
        preview.Children.Add(account);
        preview.Children.Add(authorization);
        AutomationProperties.SetAutomationId(status, "FeedbackStatus");
        AutomationProperties.SetAutomationId(issueLink, "FeedbackIssueLink");
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(editor);
        content.Children.Add(preview);
        content.Children.Add(status);
        content.Children.Add(issueLink);
        scroll = new ScrollViewer
        {
            Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Content = scroll;
        FitToWindow();
        Loaded += (_, _) =>
        {
            root.Changed += OnRootChanged;
            FitToWindow();
        };
        Unloaded += (_, _) => root.Changed -= OnRootChanged;
        Closing += (_, args) => { if (submitting && !exiting) args.Cancel = true; };
        Closed += (_, _) => { exiting = true; closed.Cancel(); closed.Dispose(); };
        PrimaryButtonClick += OnPrimary;
        SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            report = null;
            preview.Visibility = Visibility.Collapsed;
            editor.Visibility = Visibility.Visible;
            status.IsOpen = false;
            issueLink.Visibility = Visibility.Collapsed;
            UpdateActions();
            scroll.ChangeView(null, 0, null, true);
            subject.Focus(FocusState.Programmatic);
        };
        UpdateActions();

        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => FitToWindow();

        void FitToWindow()
        {
            // Leave 24 DIPs of native dialog padding per side and room around the shell.
            scroll.Width = Math.Clamp(root.Size.Width - 88, 0, DialogWidth - 48);
            scroll.MaxHeight = Math.Clamp(root.Size.Height - 200, 120, 520);
            kind.Width = Math.Min(KindWidth, scroll.Width);
            details.MinHeight = Math.Clamp(scroll.MaxHeight / 3, 108, 172);
        }
    }

    internal void CancelForExit()
    {
        if (exiting) return;
        exiting = true;
        closed.Cancel();
        Hide();
    }

    private async void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (busy || published) return;
        if (report is null)
        {
            await RunAsync(async token =>
            {
                report = FeedbackReport.Create(SelectedKind,
                    subject.Text, details.Text, environment, includeDiagnostics.IsChecked == true ? diagnostics : null);
                previewTitle.Text = report.Title;
                previewBody.Text = report.Body;
                editor.Visibility = Visibility.Collapsed;
                preview.Visibility = Visibility.Visible;
                scroll.ChangeView(null, 0, null, true);
                login = await client.ConnectedAccountAsync(token);
            });
            return;
        }
        if (!client.IsConfigured)
        {
            await OpenManualAsync();
            return;
        }
        if (login is null)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(closed.Token);
            authorizationCancellation = cancellation;
            try
            {
                await RunAsync(async _ =>
                {
                    var token = cancellation.Token;
                    try
                    {
                        ShowStatus("正在取得授权码…", InfoBarSeverity.Informational);
                        var code = await client.StartAuthorizationAsync(token);
                        status.IsOpen = false;
                        deviceCode.Text = code.UserCode;
                        authorization.Visibility = Visibility.Visible;
                        authorization.StartBringIntoView();
                        login = await client.CompleteAuthorizationAsync(code, token);
                        if (!token.IsCancellationRequested) ShowStatus("GitHub 已连接，请确认是否公开提交。", InfoBarSeverity.Success);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    { manual.Visibility = Visibility.Visible; }
                    finally { authorization.Visibility = Visibility.Collapsed; deviceCode.Text = ""; }
                });
            }
            finally { authorizationCancellation = null; }
            return;
        }

        var confirmedReport = report;
        var confirmedAccount = login;
        submitting = true;
        try
        {
            await RunAsync(async token =>
            {
                ShowStatus("正在提交反馈…", InfoBarSeverity.Informational);
                var issue = await client.SubmitAsync(confirmedReport, confirmedAccount, token);
                published = true;
                ShowStatus("反馈已提交。", InfoBarSeverity.Success);
                ShowLink(UiText.Format($"查看 Issue #{issue.Number}"), issue.Link);
            });
        }
        finally { submitting = false; }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy) return;
        var token = closed.Token;
        busy = true;
        status.IsOpen = false;
        UpdateActions();
        try { await action(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
                ShowFailure(error is FeedbackException feedback ? feedback.Failure
                    : submitting ? FeedbackFailure.SubmissionUnknown : FeedbackFailure.ServiceUnavailable);
        }
        finally
        {
            busy = false;
            if (!token.IsCancellationRequested) UpdateActions();
        }
    }

    private void ShowFailure(FeedbackFailure failure)
    {
        if (failure is FeedbackFailure.SignInRequired)
            login = null;
        if (failure is FeedbackFailure.SubmissionUnknown)
        {
            unknownSubmission = true;
            ShowLink("查看 GitHub 反馈", GitHubFeedbackClient.IssuesList);
        }
        else if (report is not null && failure != FeedbackFailure.InvalidReport)
            manual.Visibility = Visibility.Visible;
        ShowStatus(failure switch
        {
            FeedbackFailure.NotConfigured => "GitHub 应用暂不可授权。",
            FeedbackFailure.SignInRequired => "请重新连接 GitHub。",
            FeedbackFailure.AuthorizationExpired => "授权码已过期，请重新连接。",
            FeedbackFailure.AuthorizationDenied => "GitHub 未授予访问权限。",
            FeedbackFailure.RateLimited => "GitHub 请求过多，请稍后重试。",
            FeedbackFailure.RepositoryUnavailable => "GitHub 未允许此账号向项目提交反馈。",
            FeedbackFailure.InvalidReport => "请检查标题和正文后再试。",
            FeedbackFailure.SubmissionUnknown => "暂无法确认是否提交成功。请先到 GitHub 核对。",
            FeedbackFailure.Storage => "无法访问本机 GitHub 授权记录。",
            _ => "暂时无法连接 GitHub，请稍后重试。"
        }, InfoBarSeverity.Warning);
    }

    private void UpdateActions()
    {
        var hasPreview = report is not null;
        PrimaryButtonText = published || unknownSubmission ? "" : UiText.Get(!hasPreview ? "预览" : !client.IsConfigured
            ? "打开 GitHub" : login is null ? "连接 GitHub" : "公开提交");
        SecondaryButtonText = hasPreview && !published && !unknownSubmission ? UiText.Get("返回编辑") : "";
        CloseButtonText = UiText.Get(published || unknownSubmission ? "关闭" : "取消");
        IsPrimaryButtonEnabled = !busy && (hasPreview || (!string.IsNullOrWhiteSpace(subject.Text) && !string.IsNullOrWhiteSpace(details.Text)));
        IsSecondaryButtonEnabled = !busy;
        copy.IsEnabled = !busy;
        disconnect.Visibility = login is not null ? Visibility.Visible : Visibility.Collapsed;
        disconnect.IsEnabled = !busy;
        manual.IsEnabled = !busy && !published && !unknownSubmission;
        account.Text = login is null ? "" : UiText.Format($"GitHub 账号 · @{login}");
        account.Visibility = account.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool CopyFeedback(bool includeTitle)
    {
        if (report is not { } selected) return false;
        try
        {
            var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            data.SetText(includeTitle ? $"{selected.Title}\n\n{selected.Body}" : selected.Body);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            ShowStatus(includeTitle ? "反馈已复制。" : "正文已复制。请在 GitHub 填写标题并粘贴正文。", InfoBarSeverity.Informational);
            return true;
        }
        catch (Exception)
        {
            ShowStatus("无法复制，请选中文字后复制。", InfoBarSeverity.Warning);
            return false;
        }
    }

    private Task OpenManualAsync() => RunAsync(async token =>
    {
        if (!CopyFeedback(includeTitle: false)) return;
        var uri = GitHubFeedbackClient.ManualIssueForm;
        if (!await Launcher.LaunchUriAsync(uri) && !token.IsCancellationRequested)
        {
            ShowStatus("无法打开浏览器。", InfoBarSeverity.Warning);
            ShowLink("打开 GitHub", uri);
        }
    });

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        status.Message = UiText.Get(message);
        status.Severity = severity;
        status.IsOpen = true;
        status.StartBringIntoView();
    }

    private void ShowLink(string text, Uri link)
    {
        issueLink.Content = UiText.Get(text);
        issueLink.NavigateUri = link;
        issueLink.Visibility = Visibility.Visible;
        issueLink.StartBringIntoView();
    }

    private static TextBox Field(string caption, string id, int limit)
    {
        var result = new TextBox { Header = UiText.Get(caption), MaxLength = limit };
        AutomationProperties.SetAutomationId(result, id);
        AutomationProperties.SetName(result, UiText.Get(caption));
        return result;
    }

    private static TextBlock Label(string text) => new() { Text = UiText.Get(text), TextWrapping = TextWrapping.Wrap };

    private FeedbackKind SelectedKind => kind.OptionIndex == 1 ? FeedbackKind.Suggestion : FeedbackKind.Problem;
}
