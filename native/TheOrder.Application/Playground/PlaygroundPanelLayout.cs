namespace TheOrder.Application.Playground;

public enum PlaygroundPanelPriority { Sessions, Settings }

public readonly record struct PlaygroundPanelLayout(
    double WorkspaceWidth, bool SessionsOpen, bool SettingsOpen,
    double SessionsPaneWidth, double SettingsPaneWidth, double ReaderWidth);

// The same widths drive both native SplitViews and the reader's measure bound.
// Automatic suppression never changes the user's preferred open states.
public static class PlaygroundPanelLayoutPolicy
{
    public const double SessionsWidth = 220;
    public const double SettingsWidth = 288;
    public const double MinimumDualReaderWidth = 400;
    public const double MinimumSingleReaderWidth = 360;
    public const double DualPanelWidth = SessionsWidth + SettingsWidth + MinimumDualReaderWidth;

    public static PlaygroundPanelLayout Calculate(double width, bool sessionsPreferredOpen,
        bool settingsPreferredOpen, PlaygroundPanelPriority priority)
    {
        if (!double.IsFinite(width) || width <= 0) return new(0, false, false, 0, 0, 0);
        bool sessions = sessionsPreferredOpen, settings = settingsPreferredOpen;
        if (sessions && settings && width < DualPanelWidth)
        {
            sessions = priority == PlaygroundPanelPriority.Sessions;
            settings = !sessions;
        }

        // At very small sizes the opened pane also shrinks. The main document
        // keeps the larger share of the viewport instead of being covered.
        double readerFloor = Math.Min(MinimumSingleReaderWidth, width * .55);
        double sessionsWidth = Math.Min(SessionsWidth, width - readerFloor);
        double settingsWidth = Math.Min(SettingsWidth, width - readerFloor);
        double readerWidth = width - (sessions ? sessionsWidth : 0) - (settings ? settingsWidth : 0);
        return new(width, sessions, settings, sessionsWidth, settingsWidth, Math.Max(0, readerWidth));
    }
}
