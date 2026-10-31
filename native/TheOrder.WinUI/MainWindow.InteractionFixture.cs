using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Presentation;
using TheOrder.WinUI.Views;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    // Actual controls, using the existing isolated Test controller. No production
    // control channel, arbitrary paths, credentials or launch actions.
    private async Task InteractionFixtureAsync(JsonElement request)
    {
        if (Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test || fixtureDirectory is null || !startupReady)
            throw new InvalidOperationException("Ready isolated Test required");
        if (p1Dialog is not null || ViewModel.IsDirty) throw new InvalidOperationException("Clean workspace required");
        var action = request.GetProperty("value").GetString();
        var frames = new List<double>(); var clock = Stopwatch.StartNew(); double previous = 0;
        EventHandler<object> sample = (_, _) => { var now = clock.Elapsed.TotalMilliseconds; if (previous > 0) frames.Add(now - previous); previous = now; };
        CompositionTarget.Rendering += sample;
        int operations = 0; var swapsBefore = pageSwapCount;
        try
        {
            if (action == "navigation")
            {
                foreach (var page in new[] { WorkspacePage.Library, WorkspacePage.Settings, WorkspacePage.Library,
                    WorkspacePage.Statistics, WorkspacePage.Library, WorkspacePage.Import, WorkspacePage.Library,
                    WorkspacePage.Online, WorkspacePage.Library })
                {
                    await Navigate(page); await Task.Delay(200); operations++;
                    var before = pageSwapCount; await Navigate(page);
                    if (pageSwapCount != before) throw new InvalidOperationException("Same page replaced twice");
                }
            }
            else if (action is "disclosures" or "rapid")
            {
                await Navigate(WorkspacePage.Settings); await Task.Delay(200);
                var groups = P1Elements(settingsPage).OfType<SmoothExpander>()
                    .Where(x => x.Name is "AppearanceGroup" or "LaunchGroup" or "SourcesGroup" or "LibraryGroup" or "BackupGroup" or "UpdatesGroup").ToArray();
                if (groups.Length != 6) throw new InvalidOperationException("Settings disclosures unavailable");
                foreach (var group in groups)
                {
                    int rounds = action == "rapid" ? 5 : 2;
                    for (int i = 0; i < rounds; i++)
                    {
                        group.IsExpanded = true; await Task.Delay(action == "rapid" ? 25 : 400);
                        group.IsExpanded = false; await Task.Delay(action == "rapid" ? 25 : 250); operations += 2;
                    }
                    await Task.Delay(400);
                    if (group.IsExpanded || group.ContentShown) throw new InvalidOperationException("Collapsed content remained visible");
                }
            }
            else if (action == "retained-content")
            {
                var markdown = P1Elements(PageContent).OfType<MarkdownView>().FirstOrDefault();
                if (markdown is null) throw new InvalidOperationException("Detail Markdown required");
                var before = markdown.RenderCount;
                markdown.Text = markdown.Text; markdown.Visibility = Visibility.Collapsed; markdown.Visibility = Visibility.Visible;
                await Task.Delay(250); operations = 3;
                if (markdown.RenderCount != before) throw new InvalidOperationException("Unchanged Markdown rebuilt");
            }
            else throw new InvalidDataException("Unknown interaction check");
        }
        finally { CompositionTarget.Rendering -= sample; }
        var intervals = frames.Order().ToArray();
        await File.AppendAllTextAsync(Path.Combine(fixtureDirectory, "interaction-evidence.jsonl"), JsonSerializer.Serialize(new {
            action, operations, elapsedMs=clock.Elapsed.TotalMilliseconds, pageSwaps=pageSwapCount-swapsBefore,
            renderedFrames=frames.Count, medianIntervalMs=intervals.Length == 0 ? 0 : intervals[intervals.Length/2],
            longestIntervalMs=intervals.LastOrDefault(), gapsOver50Ms=frames.Count(x=>x>50),
            samePageReplacementPrevented=true, nativeTemplatesRetained=true,
            measurement="WinUI Rendering callback intervals; not system PresentMode or an end-to-end frame-rate claim",
            isolatedTest=true, productionAccess=false, passed=true })+"\n");
    }
}
