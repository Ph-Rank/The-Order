using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

public sealed partial class DiscoveryFeedView
{
    private void ConfigureRankingFilters()
    {
        var minVotes = FilterNumber("最低票数", 1, 1_000_000);
        var maxVotes = FilterNumber("最高票数", 1, 1_000_000);
        var from = FilterNumber("起始年份", 1900, 2100);
        var through = FilterNumber("截止年份", 1900, 2100);
        var platform = FilterChoice("平台", "不限", "Windows", "Mac", "Nintendo Switch / Switch 2", "PlayStation", "移动设备", "Linux");
        var circulation = FilterChoice("发行类型", "不限", "商业", "同人");
        var duration = FilterChoice("参考时长", "不限", "10 小时以内", "10–30 小时", "30 小时以上");
        var language = FilterChoice("原始语言", "不限", "日语", "中文", "英语");
        var sources = FilterChoice("最少评分来源", "不限", "2 站", "3 站", "4 站", "5 站", "6 站");
        var otherGames = new CheckBox { Content = UiText.Get("包含非视觉小说"), MinWidth = 0, VerticalAlignment = VerticalAlignment.Bottom };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        FrameworkElement[] fields = [minVotes, maxVotes, from, through, platform, circulation, duration, language, sources, otherGames];
        for (int index = 0; index < fields.Length; index++)
        {
            if (index % 2 == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(fields[index], index / 2);
            Grid.SetColumn(fields[index], index % 2);
            grid.Children.Add(fields[index]);
        }

        var heading = new TextBlock { FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Text = UiText.Get("榜单筛选") };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        var apply = new Button { Content = UiText.Get("应用"), Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] };
        var reset = new Button { Content = UiText.Get("清除筛选") };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { reset, apply } };
        var options = new StackPanel { MinWidth = 276, Spacing = 12, Children = { heading, grid, error, actions } };
        var scroller = new ScrollViewer { Content = options, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var flyout = new Flyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft, Content = scroller };
        RankingFilterButton.Flyout = flyout;
        flyout.Opening += (_, _) =>
        {
            scroller.MaxHeight = Math.Clamp((XamlRoot?.Size.Height ?? 680) - 120, 170, 510);
            var filters = viewModel.RankingFilters;
            minVotes.Value = filters.MinVotes ?? double.NaN;
            maxVotes.Value = filters.MaxVotes ?? double.NaN;
            from.Value = filters.FromYear ?? double.NaN;
            through.Value = filters.ThroughYear ?? double.NaN;
            platform.OptionIndex = (int)filters.Platform;
            circulation.OptionIndex = (int)filters.Circulation;
            duration.OptionIndex = (int)filters.Duration;
            language.OptionIndex = (int)filters.Language;
            sources.OptionIndex = filters.MinSources is { } count ? count - 1 : 0;
            sources.IsEnabled = viewModel.RankingSource == DiscoveryRankingSource.Consensus;
            otherGames.IsChecked = filters.IncludeOtherGames;
            error.Visibility = Visibility.Collapsed;
        };
        apply.Click += async (_, _) =>
        {
            static int? Number(NumberBox box) => double.IsNaN(box.Value) ? null : (int)box.Value;
            var filters = new DiscoveryRankingFilters
            {
                MinVotes = Number(minVotes), MaxVotes = Number(maxVotes), FromYear = Number(from), ThroughYear = Number(through),
                Platform = (RankingPlatform)platform.OptionIndex, Circulation = (RankingCirculation)circulation.OptionIndex,
                Duration = (RankingDuration)duration.OptionIndex, Language = (RankingLanguage)language.OptionIndex,
                MinSources = sources.OptionIndex > 0 ? sources.OptionIndex + 1 : null, IncludeOtherGames = otherGames.IsChecked == true
            };
            if (!filters.IsValid)
            {
                error.Text = UiText.Get(filters.MinVotes > filters.MaxVotes ? "最低票数不能超过最高票数。" : "起始年份不能晚于截止年份。");
                error.Visibility = Visibility.Visible;
                return;
            }
            flyout.Hide();
            await viewModel.SelectRankingFiltersAsync(filters);
        };
        reset.Click += async (_, _) => { flyout.Hide(); await viewModel.SelectRankingFiltersAsync(new()); };
    }

    private void UpdateRankingFilterCaption()
    {
        var count = viewModel.RankingFilters.ActiveCount(viewModel.RankingSource == DiscoveryRankingSource.Consensus);
        var caption = count == 0 ? UiText.Get("筛选") : UiText.Format($"筛选 · {count}");
        RankingFilterCaption.Text = caption;
        AutomationProperties.SetName(RankingFilterButton, caption);
    }

    private static NumberBox FilterNumber(string heading, int minimum, int maximum) => new()
    {
        Header = UiText.Get(heading), PlaceholderText = UiText.Get("不限"),
        Minimum = minimum, Maximum = maximum, Value = double.NaN, HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static SelectedFirstComboBox FilterChoice(string heading, params string[] choices) => new()
    {
        Header = UiText.Get(heading), OptionsSource = choices.Select(choice => UiText.Get(choice)).ToArray(), OptionIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
}
