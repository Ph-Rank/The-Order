using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace TheOrder.WinUI.Views;

public sealed class UserGuideChapter(string title)
{
    public string Title { get; } = title;
    internal StringBuilder Body { get; } = new();
    public override string ToString() => Title;
}

public sealed class UserGuideSection(string title) : List<UserGuideChapter>
{
    public string Title { get; } = title;
}

public sealed partial class UserGuideView : UserControl
{
    internal const double CompactWidth = 650;
    private readonly List<UserGuideChapter> chapters = [];
    private UserGuideChapter? current;
    private bool compact;

    public UserGuideView()
    {
        InitializeComponent();
        ArticleBody.CreateEmbeddedFigure = CreateFigure;
        ArticleBody.ResolveArticleLink = ResolveChapterLink;
    }

    private Action? ResolveChapterLink(string url)
    {
        if (!url.StartsWith('#')) return null;
        var chapter = chapters.Find(item => string.Equals(item.Title, url[1..], StringComparison.Ordinal));
        if (chapter is null) return null;
        return () =>
        {
            SelectChapter(chapter);
            if (!compact) ChapterList.ScrollIntoView(chapter);
            ArticleScroll.Focus(FocusState.Programmatic);
        };
    }

    private FrameworkElement? CreateFigure(string url, string alternativeText)
    {
        const string directory = "images/";
        const string extension = ".png";
        if (!url.StartsWith(directory, StringComparison.Ordinal) || !url.EndsWith(extension, StringComparison.Ordinal))
            return null;

        string name = url[directory.Length..^extension.Length];
        if (name.Length is < 1 or > 40 || name.Any(c => !char.IsAsciiLetterLower(c) && c != '-'))
            return null;

        var assembly = typeof(UserGuideView).Assembly;
        string resourceName = $"TheOrder.UserGuide.Images.{name}.png";
        if (assembly.GetManifestResourceInfo(resourceName) is null) return null;

        var image = new Image { Stretch = Stretch.Uniform };
        AutomationProperties.SetName(image, alternativeText);
        AutomationProperties.SetAutomationId(image, "UserGuideFigure:" + name);
        var frame = new Border
        {
            Child = image,
            BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["DividerBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        image.Loaded += LoadImageAsync;
        return frame;

        async void LoadImageAsync(object sender, RoutedEventArgs args)
        {
            image.Loaded -= LoadImageAsync;
            try
            {
                using var source = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidDataException("Missing help illustration.");
                using var stream = new InMemoryRandomAccessStream();
                using var writer = new DataWriter(stream);
                var bytes = new byte[checked((int)source.Length)];
                source.ReadExactly(bytes);
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
                stream.Seek(0);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                // The illustrations were captured at 200% scaling. Keep narrow crops at their original reading size.
                image.MaxWidth = bitmap.PixelWidth / 2d;
                image.Source = bitmap;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                frame.Child = new TextBlock { Text = alternativeText, TextWrapping = TextWrapping.Wrap };
            }
        }
    }

    internal void EnsureContent()
    {
        if (chapters.Count != 0) return;
        using var stream = typeof(UserGuideView).Assembly.GetManifestResourceStream("TheOrder.UserGuide.zh-Hans.md")
            ?? throw new InvalidOperationException("Missing bundled user guide.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var sections = new List<UserGuideSection>();
        UserGuideSection? section = null;
        UserGuideChapter? chapter = null;
        // Level-one and level-two headings define the directory. Deeper headings stay in the article.
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                section = new UserGuideSection(line[2..].Trim());
                sections.Add(section);
                chapter = null;
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal) && section is not null)
            {
                chapter = new UserGuideChapter(line[3..].Trim());
                section.Add(chapter);
                chapters.Add(chapter);
            }
            else
            {
                chapter?.Body.AppendLine(line);
            }
        }

        ChapterGroups.Source = sections;
        CompactChapters.OptionsSource = chapters;
        SelectChapter(chapters[0]);
    }

    private void OnChapterSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ChapterList.SelectedItem is UserGuideChapter chapter) SelectChapter(chapter);
    }

    private void OnCompactChapterSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CompactChapters.SelectedOption is UserGuideChapter chapter) SelectChapter(chapter);
    }

    private void SelectChapter(UserGuideChapter chapter)
    {
        if (ReferenceEquals(current, chapter)) return;
        bool switching = current is not null;
        current = chapter;
        ChapterList.SelectedItem = chapter;
        CompactChapters.SelectedOption = chapter;
        ArticleScroll.ChangeView(null, 0, null, true);
        ArticleTitle.Text = chapter.Title;
        ArticleBody.Text = chapter.Body.ToString().Trim();
        if (switching) InteractionMotion.RevealContent(ArticleColumn);
    }

    private void OnGuideSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < CompactWidth;
        if (compact == narrow) return;
        compact = narrow;
        RailColumn.Width = new GridLength(compact ? 0 : 234);
        ChapterRail.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactChapters.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        ArticleTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ArticleScroll.Padding = compact ? new Thickness(16, 0, 16, 0) : new Thickness(28, 0, 24, 0);
        if (!compact && current is not null) ChapterList.ScrollIntoView(current);
    }
}
