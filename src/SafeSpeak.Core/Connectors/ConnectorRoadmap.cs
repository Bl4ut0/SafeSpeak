namespace SafeSpeak.Core.Connectors;

public enum ConnectorAvailability
{
    Available,
    Planned,
    AccessRequired
}

public sealed record ConnectorRoadmapItem(
    string Id,
    string DisplayName,
    ConnectorAvailability Availability,
    string Description);

/// <summary>
/// Product-level connector registry. Planned entries are not exposed as working
/// connectors until an implementation passes the ISourceConnector contract tests.
/// </summary>
public static class ConnectorRoadmap
{
    public static IReadOnlyList<ConnectorRoadmapItem> All { get; } =
    [
        new("offline-simulator", "Offline test source", ConnectorAvailability.Available,
            "Generates local events for moderation and speech testing."),
        new("desktop-relay", "SafeSpeak desktop relay", ConnectorAvailability.Planned,
            "Receives normalized events from the desktop app on the local network."),
        new("youtube-live", "YouTube Live", ConnectorAvailability.Planned,
            "Direct connector using the official YouTube Live APIs."),
        new("twitch", "Twitch", ConnectorAvailability.Planned,
            "Direct connector using official Twitch authentication and chat APIs."),
        new("kick", "Kick", ConnectorAvailability.Planned,
            "Direct connector for Kick streaming live chat."),
        new("facebook-live", "Facebook Live", ConnectorAvailability.Planned,
            "Live video comments integration via Facebook Graph API."),
        new("instagram-live", "Instagram Live", ConnectorAvailability.Planned,
            "Live broadcast comments integration via Instagram Graph API."),
        new("x-live", "X / Twitter Live", ConnectorAvailability.Planned,
            "Live audio spaces and broadcast chat integration."),
        new("trovo", "Trovo", ConnectorAvailability.Planned,
            "Direct connector using official Trovo chat API."),
        new("tiktok-live", "TikTok LIVE", ConnectorAvailability.AccessRequired,
            "Direct mobile access requires an approved and policy-compliant TikTok integration.")
    ];
}
