using TheOrder.Application.Playground;
using Xunit;

namespace TheOrder.Tests;

public sealed class PlaygroundPanelLayoutTests
{
    [Fact]
    public void CommonWindowFitsBothPanelsWithoutCoveringTheDocument()
    {
        // A 1147 DIP workspace loses 12 DIP margins and a 1 DIP frame on each side.
        var layout = PlaygroundPanelLayoutPolicy.Calculate(1121, true, true, PlaygroundPanelPriority.Sessions);
        Assert.True(layout.SessionsOpen); Assert.True(layout.SettingsOpen);
        Assert.Equal(220, layout.SessionsPaneWidth); Assert.Equal(288, layout.SettingsPaneWidth);
        Assert.Equal(613, layout.ReaderWidth);
    }

    [Theory]
    [InlineData(PlaygroundPanelPriority.Sessions, true, false)]
    [InlineData(PlaygroundPanelPriority.Settings, false, true)]
    public void NarrowWindowKeepsTheLastOpenedPanelAndAMeaningfulReader(
        PlaygroundPanelPriority priority, bool sessions, bool settings)
    {
        var layout = PlaygroundPanelLayoutPolicy.Calculate(608, true, true, priority);
        Assert.Equal(sessions, layout.SessionsOpen); Assert.Equal(settings, layout.SettingsOpen);
        Assert.True(layout.ReaderWidth >= 608 * .55);
        AssertPartition(layout);
    }

    [Fact]
    public void ResizingDoesNotReplacePreferencesWithTheAutomaticallySuppressedState()
    {
        var narrow = PlaygroundPanelLayoutPolicy.Calculate(700, true, true, PlaygroundPanelPriority.Settings);
        Assert.False(narrow.SessionsOpen); Assert.True(narrow.SettingsOpen);
        var wide = PlaygroundPanelLayoutPolicy.Calculate(1100, true, true, PlaygroundPanelPriority.Settings);
        Assert.True(wide.SessionsOpen); Assert.True(wide.SettingsOpen);
    }

    [Fact]
    public void AnExplicitlyClosedPanelDoesNotReopenAfterResizing()
    {
        foreach (double width in new double[] { 528, 800, 1121, 1800 })
        {
            var layout = PlaygroundPanelLayoutPolicy.Calculate(width, false, true, PlaygroundPanelPriority.Sessions);
            Assert.False(layout.SessionsOpen); Assert.True(layout.SettingsOpen); AssertPartition(layout);
        }
    }

    [Fact]
    public void ClosingBothPanelsReturnsTheEntireWidthToTheReader()
    {
        var layout = PlaygroundPanelLayoutPolicy.Calculate(760, false, false, PlaygroundPanelPriority.Settings);
        Assert.False(layout.SessionsOpen); Assert.False(layout.SettingsOpen); Assert.Equal(760, layout.ReaderWidth);
    }

    [Fact]
    public void BothPanelsOnlyOpenWhenTheDocumentHasItsReservedWidth()
    {
        var below = PlaygroundPanelLayoutPolicy.Calculate(907, true, true, PlaygroundPanelPriority.Settings);
        Assert.False(below.SessionsOpen); Assert.True(below.SettingsOpen);
        var at = PlaygroundPanelLayoutPolicy.Calculate(908, true, true, PlaygroundPanelPriority.Settings);
        Assert.True(at.SessionsOpen); Assert.True(at.SettingsOpen); Assert.Equal(400, at.ReaderWidth);
    }

    [Fact]
    public void EverySupportedWidthAndPreferencePartitionsTheViewportWithoutOverflow()
    {
        foreach (double width in new double[] { 1, 200, 400, 528, 608, 700, 780, 907, 908, 1049, 1121, 1800 })
        foreach (bool sessions in new[] { false, true })
        foreach (bool settings in new[] { false, true })
        foreach (var priority in Enum.GetValues<PlaygroundPanelPriority>())
        {
            var layout = PlaygroundPanelLayoutPolicy.Calculate(width, sessions, settings, priority);
            AssertPartition(layout);
            Assert.InRange(layout.SessionsPaneWidth, 0, 220); Assert.InRange(layout.SettingsPaneWidth, 0, 288);
            if (layout.SessionsOpen && layout.SettingsOpen) Assert.True(layout.ReaderWidth >= 400);
            else Assert.True(layout.ReaderWidth >= Math.Min(360, width * .55) - .001);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnmeasuredOrInvalidViewportCannotProduceAnOpenPaneOrInvalidMeasure(double width)
    {
        var layout = PlaygroundPanelLayoutPolicy.Calculate(width, true, true, PlaygroundPanelPriority.Settings);
        Assert.Equal(0, layout.WorkspaceWidth); Assert.Equal(0, layout.ReaderWidth);
        Assert.False(layout.SessionsOpen); Assert.False(layout.SettingsOpen);
    }

    private static void AssertPartition(PlaygroundPanelLayout layout)
    {
        double occupied = layout.ReaderWidth + (layout.SessionsOpen ? layout.SessionsPaneWidth : 0)
            + (layout.SettingsOpen ? layout.SettingsPaneWidth : 0);
        Assert.InRange(Math.Abs(occupied - layout.WorkspaceWidth), 0, .001);
        Assert.InRange(layout.ReaderWidth, 0, layout.WorkspaceWidth);
    }
}
