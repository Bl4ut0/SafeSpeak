using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Audio.VoiceFramework;
using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Ipc;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ModerationPipeline _pipeline;
    private readonly ITikFinityConnector _tikFinityConnector;
    private readonly OfflineEventSimulator _simulator;
    private readonly ITtsEngine _ttsEngine;
    private readonly IAudioRouter _audioRouter;
    private readonly TtsQueue _ttsQueue;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly StreamDeckIpcServer _ipcServer;
    private readonly AppSettings _settings;

    [ObservableProperty]
    private bool _isArmed;

    [ObservableProperty]
    private bool _isAutoPlay;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isSpeaking;

    [ObservableProperty]
    private int _queueCount;

    [ObservableProperty]
    private string _connectionStatusText = "Disconnected";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _selectedAudioEndpoint = "";

    [ObservableProperty]
    private string _selectedVoice = "";

    [ObservableProperty]
    private int _speechRate = 0;

    [ObservableProperty]
    private int _speechVolume = 100;

    [ObservableProperty]
    private string _customBlockedInput = "";

    [ObservableProperty]
    private string _simulatorCustomTextInput = "";

    [ObservableProperty]
    private string _liveStatusAnnouncement = "";

    [ObservableProperty]
    private bool _isDownloadingVoice = false;

    [ObservableProperty]
    private double _voiceDownloadProgress = 0;

    public ObservableCollection<ModerationDecision> LiveFeed { get; } = new();
    public ObservableCollection<AudioEndpointInfo> AudioEndpoints { get; } = new();
    public ObservableCollection<VoiceInfo> Voices { get; } = new();
    public IReadOnlyList<NeuralVoiceCatalogItem> DownloadableVoices => LocalNeuralVoiceManager.AvailableCatalog;

    public ModerationConfig Config => _pipeline.Config;
    public ScreenReaderAnnouncer Announcer => _announcer;
    public string IntegratedReaderStatus => _announcer.IsEnhancedAccessibilityEnabled
        ? "SafeSpeak spoken guidance is enabled"
        : "SafeSpeak spoken guidance is disabled; Windows Narrator and other UI Automation readers remain supported";

    private readonly LocalNeuralVoiceManager _neuralVoiceManager;
    private readonly VoicePackageManager _packageManager;

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _pipeline = new ModerationPipeline(_settings.CreateModerationConfig());
        _tikFinityConnector = new TikFinityWebSocketClient();
        _simulator = new OfflineEventSimulator();
        _neuralVoiceManager = new LocalNeuralVoiceManager();
        _packageManager = new VoicePackageManager();
        _ttsEngine = new ModularTtsEngine(_neuralVoiceManager, _packageManager);
        _audioRouter = new WasapiAudioRouter();
        _ttsQueue = new TtsQueue(_ttsEngine, _audioRouter);
        SelectedAudioEndpoint = _settings.SelectedAudioEndpointId ?? string.Empty;
        SelectedVoice = _settings.SelectedVoiceName ?? string.Empty;
        SpeechRate = Math.Clamp(_settings.SpeechRate, -5, 5);
        SpeechVolume = Math.Clamp(_settings.SpeechVolume, 0, 100);
        _announcer = new ScreenReaderAnnouncer();
        _announcer.IsEnhancedAccessibilityEnabled = _settings.IsIntegratedReaderEnabled;

        _ipcServer = new StreamDeckIpcServer(
            stateProvider: GetIpcState,
            commandHandler: HandleIpcCommandAsync
        );

        WireEvents();
        LoadSystemAudioAndVoices();

        _ipcServer.Start();

    }

    private void WireEvents()
    {
        _tikFinityConnector.MessageReceived += async (_, msg) => await HandleIncomingMessageAsync(msg);
        _tikFinityConnector.StateChanged += (_, e) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                IsConnected = e.State == ConnectionState.Connected;
                ConnectionStatusText = $"{e.State} ({_tikFinityConnector.EndpointUrl})";
                if (IsConnected)
                {
                    _announcer.PlayCue(SoundCueType.TikFinityConnected);
                    AnnounceState($"TikFinity Connected on {_tikFinityConnector.EndpointUrl}");
                }
                else if (e.State == ConnectionState.Disconnected)
                {
                    _announcer.PlayCue(SoundCueType.TikFinityDisconnected);
                    AnnounceState("TikFinity Disconnected");
                }
            });
        };

        _simulator.MessageReceived += async (_, msg) => await HandleIncomingMessageAsync(msg);

        _ttsQueue.StateChanged += (_, e) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                IsArmed = e.IsArmed;
                IsAutoPlay = e.IsAutoPlay;
                IsPaused = e.IsPaused;
                IsSpeaking = e.IsSpeaking;
                QueueCount = e.QueueCount;
            });
        };

        _ttsQueue.PlaybackStarted += (_, decision) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                IsSpeaking = true;
            });
        };

        _ttsQueue.PlaybackFinished += (_, decision) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                IsSpeaking = false;
            });
        };
    }

    private void LoadSystemAudioAndVoices()
    {
        AudioEndpoints.Clear();
        foreach (var endpoint in _audioRouter.GetOutputEndpoints())
        {
            AudioEndpoints.Add(endpoint);
        }

        if (string.IsNullOrEmpty(SelectedAudioEndpoint) || !AudioEndpoints.Any(e => e.Id == SelectedAudioEndpoint))
        {
            SelectedAudioEndpoint = AudioEndpoints.FirstOrDefault(e => e.IsDefault)?.Id
                ?? AudioEndpoints.FirstOrDefault()?.Id
                ?? string.Empty;
        }
        _audioRouter.SelectEndpoint(string.IsNullOrEmpty(SelectedAudioEndpoint) ? null : SelectedAudioEndpoint);

        Voices.Clear();
        foreach (var voice in _ttsEngine.GetAvailableVoices())
        {
            Voices.Add(voice);
        }

        if (string.IsNullOrEmpty(SelectedVoice) || !Voices.Any(v => v.Id == SelectedVoice))
        {
            // Pick highest quality natural/neural voice first!
            var bestVoice = Voices.FirstOrDefault(v => v.IsNaturalNeural) ?? Voices.FirstOrDefault();
            if (bestVoice != null)
            {
                SelectedVoice = bestVoice.Id;
            }
        }
        _ttsQueue.SelectedVoice = string.IsNullOrEmpty(SelectedVoice) ? null : SelectedVoice;
        _ttsQueue.SpeechRate = SpeechRate;
        _ttsQueue.SpeechVolume = SpeechVolume;
    }

    partial void OnSelectedAudioEndpointChanged(string value)
    {
        _audioRouter?.SelectEndpoint(string.IsNullOrEmpty(value) ? null : value);
        _settings.SelectedAudioEndpointId = string.IsNullOrEmpty(value) ? null : value;
        _settings.Save();
    }

    partial void OnSelectedVoiceChanged(string value)
    {
        if (_ttsQueue is not null)
        {
            _ttsQueue.SelectedVoice = string.IsNullOrEmpty(value) ? null : value;
        }
        _settings.SelectedVoiceName = string.IsNullOrEmpty(value) ? null : value;
        _settings.Save();
    }

    partial void OnSpeechRateChanged(int value)
    {
        if (_ttsQueue is not null) _ttsQueue.SpeechRate = Math.Clamp(value, -5, 5);
        _settings.SpeechRate = Math.Clamp(value, -5, 5);
        _settings.Save();
    }

    partial void OnSpeechVolumeChanged(int value)
    {
        if (_ttsQueue is not null) _ttsQueue.SpeechVolume = Math.Clamp(value, 0, 100);
        _settings.SpeechVolume = Math.Clamp(value, 0, 100);
        _settings.Save();
    }

    private async Task HandleIncomingMessageAsync(ChatMessage message)
    {
        var decision = await _pipeline.ProcessMessageAsync(message);

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (LiveFeed.Count >= 100)
            {
                LiveFeed.RemoveAt(LiveFeed.Count - 1);
            }
            LiveFeed.Insert(0, decision);

            if (decision.Passed)
            {
                _announcer.PlayCue(SoundCueType.MessageApproved);
                if (!_ttsQueue.Enqueue(decision))
                {
                    AnnounceState("The approved message queue is full. A new message was not added.");
                }
            }
            else
            {
                _announcer.PlayCue(SoundCueType.MessageBlocked);
            }
        });
    }

    [RelayCommand]
    public void ToggleArm()
    {
        bool newState = !IsArmed;
        _ttsQueue.SetArmed(newState);
        IsArmed = newState;

        if (newState)
        {
            _announcer.PlayCue(SoundCueType.Armed);
            AnnounceState("SafeSpeak Armed. Moderated TTS is active.");
        }
        else
        {
            _announcer.PlayCue(SoundCueType.Disarmed);
            AnnounceState("SafeSpeak Disarmed. TTS paused.");
        }
    }

    [RelayCommand]
    public void ToggleAutoPlay()
    {
        bool newState = !IsAutoPlay;
        _ttsQueue.SetAutoPlay(newState);
        IsAutoPlay = newState;
        AnnounceState(newState ? "Automatic playback enabled" : "Manual playback mode enabled");
    }

    [RelayCommand]
    public void SkipMessage()
    {
        _ttsQueue.SkipCurrent();
        AnnounceState("Message skipped");
    }

    [RelayCommand]
    public void EmergencyPanic()
    {
        _ttsQueue.EmergencyPanicFlush();
        IsArmed = false;
        _announcer.PlayCue(SoundCueType.EmergencyPanic);
        AnnounceState("Emergency Panic Activated. Audio killed, queue cleared, system disarmed.", interrupt: true);
    }

    [RelayCommand]
    public void ClearQueue()
    {
        _ttsQueue.Clear();
        _announcer.PlayCue(SoundCueType.QueueEmpty);
        AnnounceState("TTS queue cleared");
    }

    [RelayCommand]
    public async Task PlayNextManual()
    {
        await _ttsQueue.PlayNextManualAsync();
    }

    [RelayCommand]
    public async Task ConnectTikFinity()
    {
        await _tikFinityConnector.ConnectAsync();
    }

    [RelayCommand]
    public async Task DisconnectTikFinity()
    {
        await _tikFinityConnector.DisconnectAsync();
    }

    [RelayCommand]
    public async Task RunSimulatorScenario(string scenario)
    {
        AnnounceState($"Starting simulator scenario: {scenario}");
        await _simulator.RunScenarioAsync(scenario);
    }

    [RelayCommand]
    public void InjectCustomSimulatorMessage()
    {
        if (string.IsNullOrWhiteSpace(SimulatorCustomTextInput)) return;

        _simulator.InjectMessage(SimulatorCustomTextInput, author: "custom_tester");
        SimulatorCustomTextInput = "";
    }

    [RelayCommand]
    public void AnnounceStatusPrivately()
    {
        string armedStr = IsArmed ? "Armed" : "Disarmed";
        string connStr = IsConnected ? "Connected to TikFinity" : "Disconnected from TikFinity";
        string queueStr = $"{QueueCount} messages in queue";
        string outputStr = $"Audio route: {_audioRouter.SelectedEndpointId ?? "Default Device"}";

        string announcement = $"SafeSpeak {armedStr}. {connStr}. {queueStr}. {outputStr}.";
        AnnounceState(announcement, interrupt: true);
    }

    [RelayCommand]
    public void AddCustomBlockedTerm()
    {
        if (string.IsNullOrWhiteSpace(CustomBlockedInput)) return;

        if (!Config.CustomBlockedTerms.Contains(CustomBlockedInput))
        {
            Config.CustomBlockedTerms.Add(CustomBlockedInput);
            PersistModerationSettings();
            AnnounceState($"Added blocked term: {CustomBlockedInput}");
        }
        CustomBlockedInput = "";
    }

    [RelayCommand]
    public void OpenVirtualCableGuide()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://vb-audio.com/Cable/",
                UseShellExecute = true
            });
            AnnounceState("Opening VB-Audio Virtual Cable download page in your browser.");
        }
        catch { }
    }

    [RelayCommand]
    public void RerunAccessibilityWizard()
    {
        Views.AccessibilitySetupDialog? wizard = null;
        var setupViewModel = new AccessibilitySetupViewModel(_settings, _announcer, () =>
        {
            OnPropertyChanged(nameof(IntegratedReaderStatus));
            AnnounceState("Integrated reader preference updated successfully.");
            wizard?.Close();
        });

        wizard = new Views.AccessibilitySetupDialog(setupViewModel)
        {
            Owner = Application.Current?.MainWindow
        };
        wizard.ShowDialog();
    }

    [RelayCommand]
    public async Task DownloadVoicePackage(NeuralVoiceCatalogItem item)
    {
        if (IsDownloadingVoice) return;

        IsDownloadingVoice = true;
        VoiceDownloadProgress = 0;
        AnnounceState($"Downloading natural neural voice: {item.DisplayName}...");

        var progress = new Progress<double>(p => VoiceDownloadProgress = p);
        bool success = await _neuralVoiceManager.DownloadVoicePackageAsync(item, progress);

        IsDownloadingVoice = false;

        if (success)
        {
            LoadSystemAudioAndVoices();
            SelectedVoice = item.Id;
            AnnounceState($"Voice {item.DisplayName} installed and activated!");
        }
        else
        {
            AnnounceState($"Failed to download voice package {item.DisplayName}. Check internet connection.");
        }
    }

    [RelayCommand]
    public async Task ImportVoicePackage()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select SafeSpeak Voice Package Archive",
                Filter = "SafeSpeak Voice Packages (*.safespeak-voice;*.zip)|*.safespeak-voice;*.zip|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                AnnounceState("Importing voice package...");
                var packageInfo = await _packageManager.ImportPackageFromZipAsync(dialog.FileName);

                LoadSystemAudioAndVoices();
                SelectedVoice = packageInfo.Manifest.Id;

                AnnounceState($"Successfully imported voice: {packageInfo.Manifest.DisplayName} by {packageInfo.Manifest.Author}!");
            }
        }
        catch (Exception ex)
        {
            AnnounceState($"Failed to import voice pack: {ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenCustomPacksFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _packageManager.VoicePacksRoot,
                UseShellExecute = true
            });
            AnnounceState("Opening SafeSpeak custom voice packs directory");
        }
        catch { }
    }

    [RelayCommand]
    public void OpenVoicesFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _neuralVoiceManager.VoicesDirectory,
                UseShellExecute = true
            });
            AnnounceState("Opening SafeSpeak neural voices folder");
        }
        catch { }
    }

    public void AnnounceState(string text, bool interrupt = false)
    {
        LiveStatusAnnouncement = text;
        _announcer.Announce(text, interrupt);
    }

    private IpcStateBroadcast GetIpcState()
    {
        return new IpcStateBroadcast
        {
            IsArmed = IsArmed,
            IsAutoPlay = IsAutoPlay,
            IsPaused = IsPaused,
            IsSpeaking = IsSpeaking,
            QueueCount = QueueCount,
            ConnectionState = ConnectionStatusText,
            AudienceMode = Config.AudienceMode.ToString(),
            Strictness = Config.Strictness.ToString(),
            EnglishOnly = Config.EnglishOnly,
            RejectMixedScripts = Config.RejectMixedScripts,
            SpeakUsernames = Config.SpeakUsernames,
            AiClassificationEnabled = Config.AiClassificationEnabled
        };
    }

    private Task<string> HandleIpcCommandAsync(string command, string parameter)
    {
        return Application.Current.Dispatcher.Invoke(async () =>
        {
            switch (command.ToLowerInvariant())
            {
                case "arm":
                    _ttsQueue.SetArmed(true);
                    IsArmed = true;
                    _announcer.PlayCue(SoundCueType.Armed);
                    AnnounceState("SafeSpeak Armed");
                    return "Armed";

                case "disarm":
                    _ttsQueue.SetArmed(false);
                    IsArmed = false;
                    _announcer.PlayCue(SoundCueType.Disarmed);
                    AnnounceState("SafeSpeak Disarmed");
                    return "Disarmed";

                case "toggle_arm":
                    ToggleArm();
                    return IsArmed ? "Armed" : "Disarmed";

                case "toggle_autoplay":
                    ToggleAutoPlay();
                    return IsAutoPlay ? "AutoPlayEnabled" : "AutoPlayDisabled";

                case "toggle_pause":
                    bool newPaused = !IsPaused;
                    _ttsQueue.SetPaused(newPaused);
                    IsPaused = newPaused;
                    AnnounceState(newPaused ? "TTS Queue Paused" : "TTS Queue Resumed");
                    return newPaused ? "Paused" : "Resumed";

                case "pause":
                    _ttsQueue.SetPaused(true);
                    IsPaused = true;
                    AnnounceState("TTS Queue Paused");
                    return "Paused";

                case "resume":
                    _ttsQueue.SetPaused(false);
                    IsPaused = false;
                    AnnounceState("TTS Queue Resumed");
                    return "Resumed";

                case "toggle_english":
                    Config.EnglishOnly = !Config.EnglishOnly;
                    PersistModerationSettings();
                    AnnounceState(Config.EnglishOnly ? "English-Only mode enabled" : "English-Only mode disabled");
                    return Config.EnglishOnly ? "EnglishOnlyEnabled" : "EnglishOnlyDisabled";

                case "toggle_mixedscripts":
                    Config.RejectMixedScripts = !Config.RejectMixedScripts;
                    PersistModerationSettings();
                    AnnounceState(Config.RejectMixedScripts ? "Mixed script rejection enabled" : "Mixed script rejection disabled");
                    return Config.RejectMixedScripts ? "MixedScriptsBlocked" : "MixedScriptsAllowed";

                case "toggle_usernames":
                    Config.SpeakUsernames = !Config.SpeakUsernames;
                    PersistModerationSettings();
                    AnnounceState(Config.SpeakUsernames ? "Username speech enabled" : "Username speech disabled");
                    return Config.SpeakUsernames ? "UsernamesEnabled" : "UsernamesDisabled";

                case "toggle_aiclassifier":
                    Config.AiClassificationEnabled = !Config.AiClassificationEnabled;
                    PersistModerationSettings();
                    AnnounceState(Config.AiClassificationEnabled ? "AI Intent Classifier enabled" : "AI Intent Classifier disabled");
                    return Config.AiClassificationEnabled ? "AiClassifierEnabled" : "AiClassifierDisabled";

                case "cycle_audience":
                    Config.AudienceMode = Config.AudienceMode switch
                    {
                        AudienceMode.All => AudienceMode.FollowersOnly,
                        AudienceMode.FollowersOnly => AudienceMode.SubscribersOnly,
                        AudienceMode.SubscribersOnly => AudienceMode.ModeratorsOnly,
                        AudienceMode.ModeratorsOnly => AudienceMode.All,
                        _ => AudienceMode.All
                    };
                    PersistModerationSettings();
                    AnnounceState($"Audience filter set to: {Config.AudienceMode}");
                    return Config.AudienceMode.ToString();

                case "cycle_strictness":
                    Config.Strictness = Config.Strictness switch
                    {
                        ModerationStrictness.Standard => ModerationStrictness.High,
                        ModerationStrictness.High => ModerationStrictness.Maximum,
                        ModerationStrictness.Maximum => ModerationStrictness.Standard,
                        _ => ModerationStrictness.High
                    };
                    PersistModerationSettings();
                    AnnounceState($"Moderation strictness set to: {Config.Strictness}");
                    return Config.Strictness.ToString();

                case "skip":
                    SkipMessage();
                    return "Skipped";

                case "panic":
                    EmergencyPanic();
                    return "PanicExecuted";

                case "clear":
                    ClearQueue();
                    return "QueueCleared";

                case "next":
                    await PlayNextManual();
                    return "PlayedNext";

                case "status":
                    AnnounceStatusPrivately();
                    return "StatusAnnounced";

                default:
                    return "UnknownCommand";
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        SaveSettings();
        _ipcServer.Dispose();
        await _tikFinityConnector.DisposeAsync();
        await _simulator.DisposeAsync();
        await _ttsQueue.DisposeAsync();
        _ttsEngine.Dispose();
        _audioRouter.Dispose();
        _announcer.Dispose();
    }

    private void SaveSettings()
    {
        _settings.CaptureModerationConfig(Config);
        _settings.SelectedAudioEndpointId = string.IsNullOrEmpty(SelectedAudioEndpoint) ? null : SelectedAudioEndpoint;
        _settings.SelectedVoiceName = string.IsNullOrEmpty(SelectedVoice) ? null : SelectedVoice;
        _settings.SpeechRate = Math.Clamp(SpeechRate, -5, 5);
        _settings.SpeechVolume = Math.Clamp(SpeechVolume, 0, 100);
        _settings.Save();
    }

    private void PersistModerationSettings()
    {
        _settings.CaptureModerationConfig(Config);
        _settings.Save();
        OnPropertyChanged(nameof(Config));
    }
}
