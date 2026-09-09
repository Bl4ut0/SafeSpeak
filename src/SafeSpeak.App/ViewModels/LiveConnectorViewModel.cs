using CommunityToolkit.Mvvm.ComponentModel;
using SafeSpeak.Core.Connectors;

namespace SafeSpeak.App.ViewModels;

/// <summary>One independently configured and switchable live input.</summary>
public sealed partial class LiveConnectorViewModel : ObservableObject
{
    internal LiveConnectorViewModel(SourceConnectorHost host, bool isConfigured, bool isEnabled)
    {
        Host = host;
        IsConfigured = isConfigured;
        IsEnabled = isConfigured && isEnabled;
        State = host.State;
        StatusDetail = host.EndpointDescription;
    }

    internal SourceConnectorHost Host { get; }

    public string Id => Host.Descriptor.Id;
    public string DisplayName => Host.Descriptor.DisplayName;
    public string PlatformName => Host.Descriptor.ProviderName;
    public string EndpointDescription => Host.EndpointDescription;
    public bool IsConnected => State == ConnectionState.Connected;
    public bool CanToggle => IsConfigured && !IsBusy;
    public string ToggleName => IsEnabled
        ? $"Stop reading {DisplayName}"
        : $"Read chat from {DisplayName}";
    public string StatusText => !IsConfigured
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
        $"{DisplayName}. {StatusText}. {StatusDetail}";
    public string ConfigurationStateText => IsConfigured
        ? "Enabled in Settings"
        : "Disabled in Settings";
    public string ConfigurationActionText => IsConfigured
        ? $"Disable {DisplayName}"
        : $"Enable {DisplayName}";
    public string ConfigurationCardAutomationName =>
        $"{DisplayName}. {ConfigurationStateText}. Press Enter to {ConfigurationActionText.ToLowerInvariant()}.";
    public string ConfigurationCardHelpText => IsConfigured
        ? $"Disables {DisplayName} and removes it from the Live connector list. A connected session will be stopped."
        : Id == TikTokLiveConnector.ConnectorDescriptor.Id
            ? "Opens an inline username listener. Enter the TikTok username twice to confirm and save it."
            : $"Enables {DisplayName}, adds it to the Live connector list, and saves the change immediately.";

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
            ? Host.EndpointDescription
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
