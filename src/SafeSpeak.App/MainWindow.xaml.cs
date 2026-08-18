using System.Windows;
using System.Windows.Controls;
using SafeSpeak.App.Accessibility;
using SafeSpeak.Core.Control;
using SafeSpeak.Infrastructure.Settings;

namespace SafeSpeak.App;

public partial class MainWindow : Window
{
    private readonly SafeSpeakRuntime _runtime;
    private readonly AppSettingsStore _settingsStore;
    private readonly ISpokenGuidanceService _spokenGuidance;
    private AppSettings _settings;
    private SafeSpeakControlState? _lastState;

    public MainWindow(
        SafeSpeakRuntime runtime,
        AppSettings settings,
        AppSettingsStore settingsStore,
        ISpokenGuidanceService spokenGuidance)
    {
        _runtime = runtime;
        _settings = settings;
        _settingsStore = settingsStore;
        _spokenGuidance = spokenGuidance;
        InitializeComponent();
        _runtime.StateChanged += Runtime_StateChanged;
        _runtime.ActivityChanged += Runtime_ActivityChanged;
        Closed += (_, _) =>
        {
            _runtime.StateChanged -= Runtime_StateChanged;
            _runtime.ActivityChanged -= Runtime_ActivityChanged;
        };

        string modeDescription = settings.AccessibilityMode switch
        {
            AccessibilityMode.FullyBlind => "Fully blind spoken-guidance mode",
            AccessibilityMode.PartiallySighted => "Partially sighted mode",
            _ => "Standard accessibility mode",
        };
        LiveStatus.Text = $"{modeDescription} active. Waiting for TikFinity on this computer.";
        UpdateState(_runtime.State);
        Loaded += (_, _) => Announce(LiveStatus.Text);
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

    private void UpdateState(SafeSpeakControlState state)
    {
        if (_lastState is not null && _lastState.Connected != state.Connected)
        {
            SetStatus(state.Connected
                ? "TikFinity connected. SafeSpeak remains disarmed until you arm it."
                : "TikFinity disconnected. TTS and automatic playback were disabled.");
        }

        ConnectionValue.Text = state.Connected ? "Connected" : "Disconnected";
        ArmedValue.Text = state.Armed ? "Armed" : "Disarmed";
        AutomaticPlaybackValue.Text = state.AutomaticPlayback ? "On" : "Off";
        QueueValue.Text = $"{state.QueueCount} {(state.QueueCount == 1 ? "message" : "messages")}" +
            (state.QueuePaused ? ", paused" : string.Empty);
        EnglishOnlyValue.Text = state.EnglishOnly ? "On" : "Off";
        _lastState = state;
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
