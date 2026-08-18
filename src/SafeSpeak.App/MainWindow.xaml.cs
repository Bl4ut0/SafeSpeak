using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SafeSpeak.App.Accessibility;
using SafeSpeak.Core.Chat;
using SafeSpeak.Core.Control;
using SafeSpeak.Core.Queueing;
using SafeSpeak.Infrastructure.Settings;
using SafeSpeak.Infrastructure.TikFinity;

namespace SafeSpeak.App;

public partial class MainWindow : Window
{
    private readonly SafeSpeakRuntime _runtime;
    private readonly TikFinityWebSocketClient _tikFinity;
    private readonly AppSettingsStore _settingsStore;
    private readonly ISpokenGuidanceService _spokenGuidance;
    private readonly DispatcherTimer _connectionLossTimer;
    private AppSettings _settings;
    private TikFinityBridgeStatus? _lastBridgeStatus;
    private bool _isLoaded;
    private bool _hasConnected;
    private bool _connectionLossAnnounced;

    public MainWindow(
        SafeSpeakRuntime runtime,
        TikFinityWebSocketClient tikFinity,
        AppSettings settings,
        AppSettingsStore settingsStore,
        ISpokenGuidanceService spokenGuidance)
    {
        _runtime = runtime;
        _tikFinity = tikFinity;
        _settings = settings;
        _settingsStore = settingsStore;
        _spokenGuidance = spokenGuidance;
        InitializeComponent();

        _connectionLossTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(8),
        };
        _connectionLossTimer.Tick += ConnectionLossTimer_Tick;

        _runtime.StateChanged += Runtime_StateChanged;
        _runtime.ActivityChanged += Runtime_ActivityChanged;
        _tikFinity.StatusChanged += TikFinity_StatusChanged;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;

        UpdateAccessibilitySummary();
        UpdateState(_runtime.State);
        UpdateBridgeStatus(_tikFinity.Status);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        OverviewTab.Focus();
        string startupStatus =
            $"SafeSpeak dashboard opened. TikFinity bridge {_runtime.ConnectionState.ToString().ToLowerInvariant()}. " +
            "TTS is disarmed. The approved queue is empty. " +
            "Use Control plus Tab to move through Overview, Approved queue, Safety and playback, TikFinity bridge, and Accessibility.";
        LiveStatus.Text = startupStatus;
        Announce(startupStatus);
        if (_runtime.ConnectionState != ConnectionState.Connected)
        {
            _connectionLossTimer.Start();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _runtime.StateChanged -= Runtime_StateChanged;
        _runtime.ActivityChanged -= Runtime_ActivityChanged;
        _tikFinity.StatusChanged -= TikFinity_StatusChanged;
        _connectionLossTimer.Stop();
        _connectionLossTimer.Tick -= ConnectionLossTimer_Tick;
    }

    private async void AccessibilitySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        bool guidanceWasEnabled = _settings.SpokenGuidanceEnabled;
        var setupWindow = new AccessibilitySetupWindow(
            _settings.AccessibilityMode,
            _spokenGuidance,
            announcePrompt: guidanceWasEnabled)
        {
            Owner = this,
        };
        if (setupWindow.ShowDialog() != true)
        {
            return;
        }

        _settings = _settings with
        {
            AccessibilityMode = setupWindow.SelectedMode,
            SpokenGuidanceEnabled = setupWindow.SpokenGuidanceEnabled,
        };
        await _settingsStore.SaveAsync(_settings);
        UpdateAccessibilitySummary();
        string message = _settings.SpokenGuidanceEnabled
            ? "Spoken guidance enabled. Accessibility preference saved."
            : "Spoken guidance disabled. Accessibility preference saved.";
        LiveStatus.Text = message;
        if (_settings.SpokenGuidanceEnabled || guidanceWasEnabled)
        {
            _spokenGuidance.Speak(message);
        }
    }

    private async void ControlButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string command })
        {
            return;
        }

        ControlResponse response = await _runtime.HandleAsync(new ControlRequest("command", command));
        SetStatus(response.Message ?? (response.Success ? "Action completed." : "Action failed."));
    }

    private void Runtime_StateChanged(object? sender, SafeSpeakControlState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateState(state));
            return;
        }

        UpdateState(state);
    }

    private void Runtime_ActivityChanged(object? sender, string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(message));
            return;
        }

        SetStatus(message);
    }

    private void TikFinity_StatusChanged(object? sender, TikFinityBridgeStatus status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateBridgeStatus(status));
            return;
        }

        UpdateBridgeStatus(status);
    }

    private void UpdateState(SafeSpeakControlState state)
    {
        ArmedValue.Text = state.Armed ? "Armed" : "Disarmed";
        AutomaticPlaybackValue.Text = state.AutomaticPlayback ? "On" : "Off";
        QueueValue.Text = $"{state.QueueCount} of {_runtime.QueueCapacity} " +
            $"{(state.QueueCount == 1 ? "message" : "messages")}" +
            (state.QueuePaused ? ", paused" : ", running");
        EnglishOnlyValue.Text = state.EnglishOnly ? "On" : "Off";
        PolicyLanguageValue.Text = state.EnglishOnly ? "English/Latin only" : "All writing systems; mixed scripts still rejected";
        UpdateQueue(_runtime.GetQueueSnapshot(), state.QueuePaused);
    }

    private void UpdateQueue(IReadOnlyList<TtsQueueItem> queue, bool paused)
    {
        var displayItems = queue
            .Select((item, index) => new DashboardQueueItem(
                index + 1,
                item.SpeakableText,
                item.Message.ReceivedAt,
                item.Message.AudienceRole))
            .ToArray();

        QueueList.ItemsSource = displayItems;
        EmptyQueueText.Visibility = displayItems.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueSummaryText.Text = displayItems.Length == 0
            ? $"The approved queue is empty and {(paused ? "paused" : "running")}."
            : $"{displayItems.Length} approved {(displayItems.Length == 1 ? "message is" : "messages are")} waiting in playback order. The queue is {(paused ? "paused" : "running")}.";
    }

    private void UpdateBridgeStatus(TikFinityBridgeStatus status)
    {
        if (_isLoaded && status.State == ConnectionState.Connected)
        {
            _connectionLossTimer.Stop();
            if (!_hasConnected || _connectionLossAnnounced)
            {
                SetStatus(_hasConnected
                    ? "TikFinity connection restored. SafeSpeak remains disarmed until you arm it."
                    : "TikFinity connected. SafeSpeak remains disarmed until you arm it.");
            }

            _hasConnected = true;
            _connectionLossAnnounced = false;
        }
        else if (_isLoaded &&
                 _lastBridgeStatus?.State == ConnectionState.Connected &&
                 status.State != ConnectionState.Connected)
        {
            _connectionLossTimer.Stop();
            _connectionLossTimer.Start();
        }

        string stateText = status.State.ToString();
        ConnectionValue.Text = stateText;
        BridgeEndpointValue.Text = status.Endpoint.AbsoluteUri;
        BridgeStateValue.Text = stateText;
        BridgeAttemptsValue.Text = status.ConnectionAttempts.ToString();
        BridgeEventsValue.Text = status.TextEventsReceived.ToString();
        BridgeAcceptedValue.Text = status.ChatMessagesAccepted.ToString();
        BridgeIgnoredValue.Text = status.EventsIgnored.ToString();
        BridgeLastEventValue.Text = status.LastChatMessageAt?.ToLocalTime().ToString("G") ?? "None received";
        BridgeLastErrorValue.Text = status.LastError ?? "None";
        _lastBridgeStatus = status;
    }

    private void ConnectionLossTimer_Tick(object? sender, EventArgs e)
    {
        _connectionLossTimer.Stop();
        if (_lastBridgeStatus?.State == ConnectionState.Connected || _connectionLossAnnounced)
        {
            return;
        }

        _connectionLossAnnounced = true;
        SetStatus(_hasConnected
            ? "TikFinity has been unavailable for 8 seconds. TTS is disarmed and SafeSpeak will keep retrying silently."
            : "TikFinity is not available. TTS is disarmed and SafeSpeak will keep retrying silently.");
    }

    private void UpdateAccessibilitySummary()
    {
        string modeDescription = _settings.AccessibilityMode switch
        {
            AccessibilityMode.FullyBlind => "Fully blind",
            AccessibilityMode.PartiallySighted => "Partially sighted",
            _ => "Standard",
        };
        string guidanceDescription = _settings.SpokenGuidanceEnabled
            ? "SafeSpeak spoken guidance enabled"
            : "SafeSpeak spoken guidance disabled";
        AccessibilityModeValue.Text = $"{modeDescription}; {guidanceDescription}";
        AccessibilitySummaryText.Text =
            $"{modeDescription} mode is selected with {guidanceDescription.ToLowerInvariant()}.";
    }

    private void SetStatus(string message)
    {
        LiveStatus.Text = message;
        Announce(message);
    }

    private void Announce(string message)
    {
        if (_settings.SpokenGuidanceEnabled)
        {
            _spokenGuidance.Speak(message);
        }
    }
}
