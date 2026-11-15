using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

internal sealed class PersonalEditDialog : ContentDialog
{
    internal PersonalEditDialog(PersonalDraft draft, Func<PersonalDraft, Task<ClientResult>> save)
    {
        Title = UiText.Get("编辑作品");
        PrimaryButtonText = UiText.Get("保存"); CloseButtonText = UiText.Get("取消");
        DefaultButton = ContentDialogButton.Primary;
        var name = Field("个人名称", "SingleEditName", draft.Name, 2000);
        var status = new SelectedFirstComboBox
        {
            Header = UiText.Get("游玩状态"), HorizontalAlignment = HorizontalAlignment.Stretch,
            OptionsSource = TheOrder.Domain.GameProgress.Ordered.Select(StatusNames.Display).ToArray(),
            OptionIndex = TheOrder.Domain.GameProgress.Ordered.IndexOf(draft.Status)
        };
        AutomationProperties.SetAutomationId(status, "SingleEditStatus");
        var favorite = new SelectedFirstComboBox
        {
            Header = UiText.Get("收藏"), HorizontalAlignment = HorizontalAlignment.Stretch,
            OptionsSource = new[] { UiText.Get("未收藏"), UiText.Get("已收藏") }, OptionIndex = draft.Favorite ? 1 : 0
        };
        AutomationProperties.SetAutomationId(favorite, "SingleEditFavorite");
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(favorite, 1); row.Children.Add(status); row.Children.Add(favorite);
        var rating = Field("个人评分 · 0–10，可留空", "SingleEditRating", draft.Rating);
        var categories = Field("分类（逗号或换行分隔）", "SingleEditCategories", draft.Categories);
        var tags = Field("个人标签（逗号或换行分隔）", "SingleEditTags", draft.Tags);
        var notes = Field("我的笔记", "SingleEditNotes", draft.Notes);
        notes.AcceptsReturn = true; notes.TextWrapping = TextWrapping.Wrap; notes.MinHeight = 80;
        var failure = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        var panel = new StackPanel { Spacing = 12 };
        foreach (var control in new UIElement[] { name, row, rating, categories, tags, notes, failure }) panel.Children.Add(control);
        var scroll = new ScrollViewer
        {
            Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Content = scroll;
        Opened += (_, _) => scroll.MaxHeight = Math.Clamp((XamlRoot?.Size.Height ?? 800) - 230, 160, 560);
        var saving = false;
        Closing += (_, args) => { if (saving) args.Cancel = true; };
        PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            saving = true; IsPrimaryButtonEnabled = false; scroll.IsEnabled = false; failure.IsOpen = false;
            try
            {
                draft.Name = name.Text; draft.Rating = rating.Text; draft.Favorite = favorite.OptionIndex == 1;
                if (status.OptionIndex >= 0) draft.Status = TheOrder.Domain.GameProgress.Ordered[status.OptionIndex];
                draft.Categories = categories.Text; draft.Tags = tags.Text; draft.Notes = notes.Text;
                if (!draft.IsDirty) { args.Cancel = false; return; }
                var result = await save(draft);
                args.Cancel = !result.IsCommitted;
                if (args.Cancel) { failure.Message = result.Message; failure.IsOpen = true; failure.StartBringIntoView(); }
            }
            catch (Exception)
            {
                failure.Message = UiText.Get("保存未能完成，请重试。修改仍保留在窗口中。"); failure.IsOpen = true;
            }
            finally
            {
                saving = false; scroll.IsEnabled = true; IsPrimaryButtonEnabled = true; deferral.Complete();
            }
        };
    }
    private static TextBox Field(string header, string id, string value, int maxLength = 0)
    {
        var field = new TextBox { Header = UiText.Get(header), Text = value, MaxLength = maxLength };
        AutomationProperties.SetAutomationId(field, id);
        AutomationProperties.SetName(field, UiText.Get(header));
        return field;
    }
}
