using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;

namespace TheOrder.WinUI.Views;

// One shared, lazy source disclosure. The page shows the selected translation;
// this section describes its provenance without repeating full original texts.
public sealed class ContentLanguageInfo : StackPanel
{
    private readonly Expander section = new SmoothExpander() { Header = "资料语言与来源", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private ContentSelection[] selections = [];
    private string? rendered;
    public bool IsExpanded { get => section.IsExpanded; set => section.IsExpanded = value; }
    internal object Evidence => new { preference = ContentLanguage.Current, expanded = IsExpanded,
        fields = selections.Select(x => new { selected = x.Selected, original = x.Original, variantCount = x.Variants.Length }).ToArray() };
    public ContentLanguageInfo()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch; SecondaryDisclosure.Apply(section); Children.Add(section);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(section, "ContentLanguageOriginals");
        section.Expanding += (_, _) => Render();
    }
    public void Show(params ContentSelection[] values)
    {
        selections = values.Where(x => x.Selected is not null).ToArray();
        Visibility = selections.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (IsExpanded) Render();
    }
    private void Render()
    {
        var key = ContentLanguage.Current + "/" + string.Join("\u001f", selections.Select(x => x.Selected));
        if (key == rendered) return;
        rendered = key;
        var body = new StackPanel { Spacing = 12, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var selection in selections)
        {
            if (selection.Selected is not { } selected) continue;
            var field = selected.Field switch { "title" => "标题", "synopsis" => "简介", "name" => "姓名", "aliases" => "别名", _ => selected.Field };
            var caption = new TextBlock { Text = field + " · " + selection.Caption, Opacity = .75, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            ToolTipService.SetToolTip(caption, selected.Identity.Provider + " / " + selected.Identity.Resource + " / " + selected.Identity.Id);
            body.Children.Add(caption);
        }
        section.Content = body;
    }
}
