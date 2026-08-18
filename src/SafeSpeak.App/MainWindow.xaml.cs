using System.Windows;
using System.Windows.Controls;
using SafeSpeak.Core.Control;
using SafeSpeak.Infrastructure.Settings;

namespace SafeSpeak.App;

public partial class MainWindow : Window
{
    private readonly SafeSpeakRuntime _runtime;
    private readonly AppSettingsStore _settingsStore;
    private AppSettings _settings;
    private SafeSpeakControlState? _lastState;

    public MainWindow(SafeSpeakRuntime runtime, AppSettings settings, AppSettingsStore settingsStore)
    {
        _runtime = runtime;
        _settings = settings;
        _settingsStore = settingsStore;
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
            AccessibilityMode.FullyBlind => "Fully blind screen-reader mode",
            AccessibilityMode.PartiallySighted => "Partially sighted mode",
            _ => "Standard accessibility mode",
        };
        LiveStatus.Text = $"{modeDescription} active. Waiting for TikFinity on this computer.";
        UpdateState(_runtime.State);
    }

    private async void AccessibilitySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var setupWindow = new AccessibilitySetupWindow(_settings.AccessibilityMode)
        {
            Owner = this,
        };
        if (setupWindow.ShowDialog() != true)
        {
            return;
        }

        _settings = _settings with { AccessibilityMode = setupWindow.SelectedMode };
        await _settingsStore.SaveAsync(_settings);
        LiveStatus.Text = "Accessibility preference saved.";
    }

    private async void ControlButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string command })
        {
            return;
        }

        ControlResponse response = await _runtime.HandleAsync(new ControlRequest("command", command));
        LiveStatus.Text = response.Message ?? (response.Success ? "Action completed." : "Action failed.");
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
            Dispatcher.Invoke(() => LiveStatus.Text = message);
            return;
        }

        LiveStatus.Text = message;
    }

    private void UpdateState(SafeSpeakControlState state)
    {
        if (_lastState is not null && _lastState.Connected != state.Connected)
        {
            LiveStatus.Text = state.Connected
                ? "TikFinity connected. SafeSpeak remains disarmed until you arm it."
                : "TikFinity disconnected. TTS and automatic playback were disabled.";
        }

        ConnectionValue.Text = state.Connected ? "Connected" : "Disconnected";
        ArmedValue.Text = state.Armed ? "Armed" : "Disarmed";
        AutomaticPlaybackValue.Text = state.AutomaticPlayback ? "On" : "Off";
        QueueValue.Text = $"{state.QueueCount} {(state.QueueCount == 1 ? "message" : "messages")}" +
            (state.QueuePaused ? ", paused" : string.Empty);
        EnglishOnlyValue.Text = state.EnglishOnly ? "On" : "Off";
        _lastState = state;
    }
}
