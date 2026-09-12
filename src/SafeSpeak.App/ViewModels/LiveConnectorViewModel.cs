using CommunityToolkit.Mvvm.ComponentModel;
using SafeSpeak.Core.Connectors;

namespace SafeSpeak.App.ViewModels;

/// <summary>One independently configured and switchable live input.</summary>
public sealed partial class LiveConnectorViewModel : ObservableObject
{
    private readonly string? _customId;
    private readonly string? _customDisplayName;
    private readonly string? _customPlatformName;
    private readonly string? _customEndpointDescription;
    private readonly string? _customShortDescription;
    private readonly string? _customHelpText;

    internal LiveConnectorViewModel(SourceConnectorHost host, bool isConfigured, bool isEnabled)
    {
        Host = host;
        IsConfigured = isConfigured;
        IsEnabled = isConfigured && isEnabled;
        State = host.State;
        StatusDetail = host.EndpointDescription;
    }

    internal LiveConnectorViewModel(
        string id,
        string displayName,
        string platformName,
        string shortDescription,
        string helpText)
    {
        _customId = id;
        _customDisplayName = displayName;
        _customPlatformName = platformName;
        _customShortDescription = shortDescription;
        _customHelpText = helpText;
        _customEndpointDescription = shortDescription;
        _statusDetail = shortDescription;
        IsConfigured = false;
        IsEnabled = false;
        State = ConnectionState.Disconnected;
    }

    internal SourceConnectorHost? Host { get; }

    public bool IsImplemented => Host is not null;
    public bool IsPlanned => !IsImplemented;
    public string? Tag => IsPlanned ? "Planned" : null;
    public bool HasTag => IsPlanned;

    public string Id => Host?.Descriptor.Id ?? _customId ?? string.Empty;
    public string DisplayName => Host?.Descriptor.DisplayName ?? _customDisplayName ?? string.Empty;
    public string PlatformName => Host?.Descriptor.ProviderName ?? _customPlatformName ?? string.Empty;
    public string EndpointDescription => Host?.EndpointDescription ?? _customEndpointDescription ?? string.Empty;
    public bool IsConnected => State == ConnectionState.Connected;
    public bool CanToggle => IsImplemented && IsConfigured && !IsBusy;
    public string ToggleName => IsEnabled
        ? $"Stop reading {DisplayName}"
        : $"Read chat from {DisplayName}";
    public string StatusText => !IsImplemented
        ? "Planned"
        : !IsConfigured
            ? "Not configured"
            : !IsEnabled
                ? "Off"
                : State switch
                {
                    ConnectionState.Connected => "Ready and listening",
                    ConnectionState.Connecting => "Connecting",
                    ConnectionState.Reconnecting => "Reconnecting",
                    ConnectionState.Faulted => "Unavailable",
                    _ => "Disconnected"
                };
    public string AccessibleStatus =>
        IsPlanned
            ? $"{DisplayName}. Planned connector. {EndpointDescription}"
            : $"{DisplayName}. {StatusText}. {StatusDetail}";
    public string ConfigurationStateText =>
        IsPlanned
            ? (_customShortDescription ?? "Planned for a future update.")
            : (IsConfigured ? "Enabled in Settings" : "Disabled in Settings");
    public string ConfigurationActionText =>
        IsPlanned
            ? "Planned"
            : (IsConfigured ? $"Disable {DisplayName}" : $"Enable {DisplayName}");
    public string ConfigurationCardAutomationName =>
        IsPlanned
            ? $"{DisplayName}. Planned connector. {ConfigurationStateText}. Not yet available."
            : (IsConfigured
                ? $"{DisplayName}. {ConfigurationStateText}. Press Enter for Edit, Delete, and Cancel options."
                : $"{DisplayName}. {ConfigurationStateText}. Press Enter to {ConfigurationActionText.ToLowerInvariant()}.");
    public string ConfigurationCardHelpText =>
        IsPlanned
            ? (_customHelpText ?? $"{DisplayName} connector is planned for a future update.")
            : (IsConfigured
                ? $"Opens options to edit or delete {DisplayName}. Delete disables it and stops any connected session."
                : Id == TikTokLiveConnector.ConnectorDescriptor.Id
                    ? "Opens an inline username listener. Enter the TikTok username twice to confirm and save it."
                    : $"Enables {DisplayName}, adds it to the Live connector list, and saves the change immediately.");

    public static LiveConnectorViewModel CreatePlanned(
        string id,
        string displayName,
        string platformName,
        string shortDescription,
        string helpText)
    {
        return new LiveConnectorViewModel(
            id,
            displayName,
            platformName,
            shortDescription,
            helpText);
    }

    public static IReadOnlyList<LiveConnectorViewModel> CreateDefaultPlannedConnectors() =>
    [
        CreatePlanned("twitch", "Twitch", "Twitch",
            "Direct IRC / EventSub chat.",
            "Twitch direct IRC and EventSub connector is planned for a future update."),
        CreatePlanned("youtube-live", "YouTube Live", "YouTube Live",
            "Official Live Chat API.",
            "YouTube Live streaming chat API connector is planned for a future update."),
        CreatePlanned("kick", "Kick", "Kick",
            "Kick live stream chat.",
            "Kick streaming live chat connector is planned for a future update."),
        CreatePlanned("facebook-live", "Facebook Live", "Facebook Live",
            "Live video comments.",
            "Facebook Live video comments connector is planned for a future update."),
        CreatePlanned("instagram-live", "Instagram Live", "Instagram Live",
            "Broadcast comments.",
            "Instagram Live broadcast comments connector is planned for a future update."),
        CreatePlanned("x-live", "X Live", "X / Twitter",
            "Spaces & live video.",
            "X Spaces and live broadcast chat connector is planned for a future update."),
        CreatePlanned("trovo", "Trovo", "Trovo",
            "Trovo live chat API.",
            "Trovo live chat API connector is planned for a future update.")
    ];

    [ObservableProperty]
    private bool _isConfigured;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private ConnectionState _state;

    [ObservableProperty]
    private string _statusDetail = string.Empty;

    internal void ApplyState(ConnectionState state, string detail)
    {
        State = state;
        StatusDetail = string.IsNullOrWhiteSpace(detail)
            ? (Host?.EndpointDescription ?? _customEndpointDescription ?? string.Empty)
            : detail;
        OnPropertyChanged(nameof(EndpointDescription));
    }

    partial void OnIsConfiguredChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ConfigurationStateText));
        OnPropertyChanged(nameof(ConfigurationActionText));
        OnPropertyChanged(nameof(ConfigurationCardAutomationName));
        OnPropertyChanged(nameof(ConfigurationCardHelpText));
        RefreshStatus();
    }

    partial void OnIsEnabledChanged(bool value) => RefreshStatus();

    partial void OnIsBusyChanged(bool value) =>
        OnPropertyChanged(nameof(CanToggle));

    partial void OnStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(IsConnected));
        RefreshStatus();
    }

    partial void OnStatusDetailChanged(string value) =>
        OnPropertyChanged(nameof(AccessibleStatus));

    private void RefreshStatus()
    {
        OnPropertyChanged(nameof(ToggleName));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(AccessibleStatus));
    }
}
