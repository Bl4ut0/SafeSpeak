using CommunityToolkit.Mvvm.ComponentModel;

namespace SafeSpeak.App.ViewModels;

/// <summary>
/// Represents a placeholder connector on the roadmap that is planned for a future update.
/// </summary>
public sealed partial class PlannedConnectorViewModel : ObservableObject
{
    public PlannedConnectorViewModel(
        string id,
        string displayName,
        string shortDescription,
        string helpText,
        string roadmapDescription,
        string? modalTitle = null)
    {
        Id = id;
        DisplayName = displayName;
        ShortDescription = shortDescription;
        HelpText = helpText;
        RoadmapDescription = roadmapDescription;
        ModalTitle = modalTitle ?? displayName;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string ShortDescription { get; }
    public string HelpText { get; }
    public string RoadmapDescription { get; }
    public string ModalTitle { get; }
    public string StatusBadge => "Coming Soon";
    public string ActionText => "Planned • Details";
    public string AutomationName => $"{DisplayName} connector. Coming soon. Press to view roadmap details.";
    public string ConfigurationCardHelpText => $"{HelpText} Press Enter to view roadmap details.";

    public static IReadOnlyList<PlannedConnectorViewModel> CreateDefaultList() =>
    [
        new("twitch", "Twitch", "Direct IRC / EventSub chat.",
            "Twitch direct IRC and EventSub connector is planned for a future update.",
            "Planned for an upcoming update. SafeSpeak will support official Twitch authentication and IRC / EventSub chat integration."),
        new("youtube-live", "YouTube Live", "Official Live Chat API.",
            "YouTube Live streaming chat API connector is planned for a future update.",
            "Planned for an upcoming update. SafeSpeak will support official YouTube Live Streaming and Live Chat APIs."),
        new("kick", "Kick", "Kick live stream chat.",
            "Kick streaming live chat connector is planned for a future update.",
            "Planned for an upcoming update. SafeSpeak will support official Kick streaming live chat integration."),
        new("facebook-live", "Facebook Live", "Live video comments.",
            "Facebook Live video comments connector is planned for a future update.",
            "Planned for an upcoming update. SafeSpeak will support Facebook Graph API live video comments integration."),
        new("instagram-live", "Instagram Live", "Broadcast comments.",
            "Instagram Live broadcast comments connector is planned for a future update.",
            "Planned for an upcoming update. SafeSpeak will support Instagram Graph API live broadcast comments integration."),
        new("x-live", "X Live", "Spaces & live video.",
            "X Spaces and live broadcast chat connector is planned for a future update.",
            "Planned for an upcoming update. SafeSpeak will support live audio spaces and broadcast chat feeds.",
            modalTitle: "X / Twitter Live"),
        new("trovo", "Trovo", "Trovo live chat API.",
            "Trovo live chat API connector is planned for a future update.",
            "Planned for an upcoming update. SafeSpeak will support official Trovo chat API integration.")
    ];
}
