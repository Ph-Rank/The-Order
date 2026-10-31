namespace TheOrder.Application.Playground;

public static class PlaygroundAuthorizationPolicy
{
    public static bool CanSend(PlaygroundSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // Enabled is retained only for old JSON readers. A saved connection
        // needs the global send grant and its Key, not a second feature switch.
        if (settings.DiscussionConsent is { } consent) return consent;
        // An old mixed or empty grant set must never become a new global grant.
        return !settings.Connections.IsDefaultOrEmpty && settings.Connections.All(connection => connection.Consented);
    }
}
