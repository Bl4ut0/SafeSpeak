using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Audio;
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
    private readonly IAudioRouter _privateAudioRouter;
    private readonly IAudioRouter _voicePreviewAudioRouter;
    private readonly PrivateVoicePreviewOutput _voicePreviewOutput;
    private readonly TtsQueue _ttsQueue;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly StreamDeckIpcServer _ipcServer;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _privateAnnouncementLock = new(1, 1);
    private bool _isInitializing = true;

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
    private string _selectedPrivateAudioEndpoint = "";

    [ObservableProperty]
    private string _selectedVoice = "";

    [ObservableProperty]
    private int _speechRate = 0;

    [ObservableProperty]
    private int _speechVolume = 100;

    [ObservableProperty]
    private int _readerSpeechRate = 3;

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

    [ObservableProperty]
    private bool _broadcastOutputEnabled = true;

    [ObservableProperty]
    private bool _privateMonitorEnabled;

    [ObservableProperty]
    private bool _mirrorApprovedMessagesToPrivateMonitor = true;

    [ObservableProperty]
    private bool _privateModerationNoticesEnabled = true;

    [ObservableProperty]
    private bool _announceChatMessages = true;

    [ObservableProperty]
    private bool _announceGifts = true;

    [ObservableProperty]
    private bool _announceFollows = true;

    [ObservableProperty]
    private bool _announceShares = true;

    [ObservableProperty]
    private bool _announceSubscriptions = true;

    [ObservableProperty]
    private bool _announceJoins;

    [ObservableProperty]
    private bool _announceLikes;

    [ObservableProperty]
    private bool _useHighContrastTheme;

    public ObservableCollection<ModerationDecision> LiveFeed { get; } = new();
    public ObservableCollection<AudioEndpointInfo> AudioEndpoints { get; } = new();
    public ObservableCollection<VoiceInfo> Voices { get; } = new();
    public bool IsKokoroInstalled => _kokoroManager.IsInstalled;
    public string KokoroInstallationStatus => IsKokoroInstalled
        ? $"Installed. {KokoroModelManager.EnglishVoices.Count} offline neural voices are available."
        : "Optional. Installs one local model with 27 English voices; speech stays on this computer.";

    public ModerationConfig Config => _pipeline.Config;
    public ScreenReaderAnnouncer Announcer => _announcer;
    public string IntegratedReaderStatus => _announcer.IsEnhancedAccessibilityEnabled
        ? "SafeSpeak spoken guidance is enabled"
        : "SafeSpeak spoken guidance is disabled; Windows Narrator and other UI Automation readers remain supported";

    private readonly KokoroModelManager _kokoroManager;

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _pipeline = new ModerationPipeline(_settings.CreateModerationConfig());
        _tikFinityConnector = new TikFinityWebSocketClient();
        _simulator = new OfflineEventSimulator();
        _kokoroManager = new KokoroModelManager();
        _ttsEngine = new ModularTtsEngine(_kokoroManager);
        _audioRouter = new WasapiAudioRouter();
        _privateAudioRouter = new WasapiAudioRouter();
        _voicePreviewAudioRouter = new WasapiAudioRouter();
        _ttsQueue = new TtsQueue(_ttsEngine, _audioRouter, privateAudioRouter: _privateAudioRouter);
        _voicePreviewOutput = new PrivateVoicePreviewOutput(_ttsEngine, _voicePreviewAudioRouter);
        SelectedAudioEndpoint = _settings.SelectedBroadcastEndpointId ?? _settings.SelectedAudioEndpointId ?? string.Empty;
        SelectedPrivateAudioEndpoint = _settings.SelectedPrivateEndpointId ?? string.Empty;
        SelectedVoice = _settings.SelectedVoiceName ?? string.Empty;
        SpeechRate = Math.Clamp(_settings.SpeechRate, -5, 5);
        SpeechVolume = Math.Clamp(_settings.SpeechVolume, 0, 100);
        ReaderSpeechRate = Math.Clamp(_settings.ReaderSpeechRate, -5, 5);
        BroadcastOutputEnabled = _settings.BroadcastOutputEnabled;
        PrivateMonitorEnabled = _settings.PrivateMonitorEnabled;
        MirrorApprovedMessagesToPrivateMonitor = _settings.MirrorApprovedMessagesToPrivateMonitor;
        PrivateModerationNoticesEnabled = _settings.PrivateModerationNoticesEnabled;
        AnnounceChatMessages = _settings.AnnounceChatMessages;
        AnnounceGifts = _settings.AnnounceGifts;
        AnnounceFollows = _settings.AnnounceFollows;
        AnnounceShares = _settings.AnnounceShares;
        AnnounceSubscriptions = _settings.AnnounceSubscriptions;
        AnnounceJoins = _settings.AnnounceJoins;
        AnnounceLikes = _settings.AnnounceLikes;
        UseHighContrastTheme = _settings.UseHighContrastTheme;
        _announcer = new ScreenReaderAnnouncer();
        _announcer.IsEnhancedAccessibilityEnabled = _settings.IsIntegratedReaderEnabled;
        _announcer.SpeechRate = ReaderSpeechRate;

        _ipcServer = new StreamDeckIpcServer(
            stateProvider: GetIpcState,
            commandHandler: HandleIpcCommandAsync
        );

        WireEvents();
        LoadSystemAudioAndVoices();

        _ipcServer.Start();
        _isInitializing = false;

    }

    private void WireEvents()
    {
        _tikFinityConnector.EventReceived += async (_, evt) => await HandleIncomingEventAsync(evt);
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
        if (string.IsNullOrEmpty(SelectedPrivateAudioEndpoint) || !AudioEndpoints.Any(e => e.Id == SelectedPrivateAudioEndpoint))
        {
            SelectedPrivateAudioEndpoint = AudioEndpoints.FirstOrDefault(e => e.IsDefault)?.Id
                ?? AudioEndpoints.FirstOrDefault()?.Id
                ?? string.Empty;
        }
        _privateAudioRouter.SelectEndpoint(string.IsNullOrEmpty(SelectedPrivateAudioEndpoint) ? null : SelectedPrivateAudioEndpoint);

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
        _ttsQueue.BroadcastOutputEnabled = BroadcastOutputEnabled;
        _ttsQueue.PrivateMonitorEnabled = PrivateMonitorEnabled;
        _ttsQueue.MirrorToPrivateMonitor = MirrorApprovedMessagesToPrivateMonitor;
        UpdateVoicePreviewSettings();
        UpdateVoicePreviewAudioEndpoint();
    }

    partial void OnSelectedAudioEndpointChanged(string value)
    {
        _audioRouter?.SelectEndpoint(string.IsNullOrEmpty(value) ? null : value);
        if (_isInitializing) return;
        _settings.SelectedAudioEndpointId = string.IsNullOrEmpty(value) ? null : value;
        _settings.SelectedBroadcastEndpointId = string.IsNullOrEmpty(value) ? null : value;
        _settings.Save();
    }

    partial void OnSelectedPrivateAudioEndpointChanged(string value)
    {
        _privateAudioRouter?.SelectEndpoint(string.IsNullOrEmpty(value) ? null : value);
        UpdateVoicePreviewAudioEndpoint();
        if (_isInitializing) return;
        _settings.SelectedPrivateEndpointId = string.IsNullOrEmpty(value) ? null : value;
        _settings.Save();
    }

    partial void OnSelectedVoiceChanged(string value)
    {
        if (_ttsQueue is not null)
        {
            _ttsQueue.SelectedVoice = string.IsNullOrEmpty(value) ? null : value;
        }
        UpdateVoicePreviewSettings();
        if (_isInitializing) return;
        _settings.SelectedVoiceName = string.IsNullOrEmpty(value) ? null : value;
        _settings.Save();
    }

    partial void OnSpeechRateChanged(int value)
    {
        if (_ttsQueue is not null) _ttsQueue.SpeechRate = Math.Clamp(value, -5, 5);
        UpdateVoicePreviewSettings();
        if (_isInitializing) return;
        _settings.SpeechRate = Math.Clamp(value, -5, 5);
        _settings.Save();
    }

    partial void OnSpeechVolumeChanged(int value)
    {
        if (_ttsQueue is not null) _ttsQueue.SpeechVolume = Math.Clamp(value, 0, 100);
        UpdateVoicePreviewSettings();
        if (_isInitializing) return;
        _settings.SpeechVolume = Math.Clamp(value, 0, 100);
        _settings.Save();
    }

    partial void OnReaderSpeechRateChanged(int value)
    {
        if (_announcer is not null) _announcer.SpeechRate = Math.Clamp(value, -5, 5);
        if (_isInitializing) return;
        _settings.ReaderSpeechRate = Math.Clamp(value, -5, 5);
        _settings.Save();
    }

    partial void OnBroadcastOutputEnabledChanged(bool value) => SaveOutputSettings();
    partial void OnPrivateMonitorEnabledChanged(bool value)
    {
        UpdateVoicePreviewAudioEndpoint();
        SaveOutputSettings();
    }
    partial void OnMirrorApprovedMessagesToPrivateMonitorChanged(bool value) => SaveOutputSettings();
    partial void OnPrivateModerationNoticesEnabledChanged(bool value) => SaveOutputSettings();
    partial void OnAnnounceChatMessagesChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceGiftsChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceFollowsChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceSharesChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceSubscriptionsChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceJoinsChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceLikesChanged(bool value) => SaveEventSettings();
    partial void OnUseHighContrastThemeChanged(bool value)
    {
        ThemeManager.Apply(value);
        if (_isInitializing) return;
        _settings.UseHighContrastTheme = value;
        _settings.Save();
    }

    private void SaveOutputSettings()
    {
        if (_ttsQueue is null) return;
        _ttsQueue.BroadcastOutputEnabled = BroadcastOutputEnabled;
        _ttsQueue.PrivateMonitorEnabled = PrivateMonitorEnabled;
        _ttsQueue.MirrorToPrivateMonitor = MirrorApprovedMessagesToPrivateMonitor;
        if (_isInitializing) return;
        _settings.BroadcastOutputEnabled = BroadcastOutputEnabled;
        _settings.PrivateMonitorEnabled = PrivateMonitorEnabled;
        _settings.MirrorApprovedMessagesToPrivateMonitor = MirrorApprovedMessagesToPrivateMonitor;
        _settings.PrivateModerationNoticesEnabled = PrivateModerationNoticesEnabled;
        _settings.Save();
    }

    private void UpdateVoicePreviewSettings()
    {
        if (_voicePreviewOutput is null) return;
        _voicePreviewOutput.VoiceId = string.IsNullOrWhiteSpace(SelectedVoice) ? null : SelectedVoice;
        _voicePreviewOutput.Rate = Math.Clamp(SpeechRate, -5, 5);
        _voicePreviewOutput.Volume = Math.Clamp(SpeechVolume, 0, 100);
    }

    private void UpdateVoicePreviewAudioEndpoint()
    {
        if (_voicePreviewAudioRouter is null) return;

        string? endpointId = PrivateMonitorEnabled
            ? SelectedPrivateAudioEndpoint
            : AudioEndpoints.FirstOrDefault(endpoint => endpoint.IsDefault)?.Id;
        _voicePreviewAudioRouter.SelectEndpoint(string.IsNullOrWhiteSpace(endpointId) ? null : endpointId);
    }

    private void SaveEventSettings()
    {
        if (_isInitializing) return;
        _settings.AnnounceChatMessages = AnnounceChatMessages;
        _settings.AnnounceGifts = AnnounceGifts;
        _settings.AnnounceFollows = AnnounceFollows;
        _settings.AnnounceShares = AnnounceShares;
        _settings.AnnounceSubscriptions = AnnounceSubscriptions;
        _settings.AnnounceJoins = AnnounceJoins;
        _settings.AnnounceLikes = AnnounceLikes;
        _settings.Save();
    }

    private async Task HandleIncomingEventAsync(LivestreamEvent liveEvent)
    {
        if (liveEvent.Type == LivestreamEventType.Chat)
        {
            if (AnnounceChatMessages) await HandleIncomingMessageAsync(liveEvent.ToChatMessage());
            return;
        }

        bool enabled = liveEvent.Type switch
        {
            LivestreamEventType.Gift => AnnounceGifts,
            LivestreamEventType.Follow => AnnounceFollows,
            LivestreamEventType.Share => AnnounceShares,
            LivestreamEventType.Subscribe => AnnounceSubscriptions,
            LivestreamEventType.Join => AnnounceJoins,
            LivestreamEventType.Like => AnnounceLikes,
            _ => false
        };
        if (!enabled) return;

        string name = string.IsNullOrWhiteSpace(liveEvent.AuthorDisplayName) ? "A viewer" : liveEvent.AuthorDisplayName;
        string spoken = liveEvent.Type switch
        {
            LivestreamEventType.Gift => $"{name} sent {liveEvent.GiftCount} {liveEvent.GiftName} gift{(liveEvent.GiftCount == 1 ? "" : "s")}",
            LivestreamEventType.Follow => $"{name} followed the stream",
            LivestreamEventType.Share => $"{name} shared the stream",
            LivestreamEventType.Subscribe => $"{name} subscribed",
            LivestreamEventType.Join => $"{name} joined",
            LivestreamEventType.Like => $"{name} liked the stream",
            _ => string.Empty
        };
        await HandleIncomingMessageAsync(new ChatMessage
        {
            Author = liveEvent.Author,
            AuthorDisplayName = string.Empty,
            RawText = spoken,
            AuthorTier = liveEvent.AuthorTier,
            IsSubscriber = liveEvent.IsSubscriber,
            IsModerator = liveEvent.IsModerator
        });
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

        if (!decision.Passed && PrivateMonitorEnabled && PrivateModerationNoticesEnabled)
        {
            await SpeakPrivateNoticeAsync($"Message from {decision.SafeAuthorDisplayName} blocked. {decision.SafeReasonDescription}");
        }
    }

    private async Task SpeakPrivateNoticeAsync(string text)
    {
        await _privateAnnouncementLock.WaitAsync();
        try
        {
            using var wave = new MemoryStream();
            await _ttsEngine.SynthesizeToWaveStreamAsync(text, wave, SelectedVoice, SpeechRate, SpeechVolume);
            await _privateAudioRouter.PlayWaveStreamAsync(wave, SpeechVolume / 100f);
        }
        catch { }
        finally { _privateAnnouncementLock.Release(); }
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
        string broadcastRoute = BroadcastOutputEnabled
            ? $"Broadcast output: {AudioEndpointFormatter.GetFriendlyName(AudioEndpoints, SelectedAudioEndpoint)}"
            : "Broadcast output disabled";
        string privateRoute = PrivateMonitorEnabled
            ? $"Private monitor: {AudioEndpointFormatter.GetFriendlyName(AudioEndpoints, SelectedPrivateAudioEndpoint)}"
            : "Private monitor disabled";

        string announcement = $"SafeSpeak {armedStr}. {connStr}. {queueStr}. {broadcastRoute}. {privateRoute}.";
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
    public void TestSelectedVoice()
    {
        VoiceInfo? voice = Voices.FirstOrDefault(
            candidate => string.Equals(candidate.Id, SelectedVoice, StringComparison.Ordinal));
        string voiceName = voice?.DisplayName ?? "the selected SafeSpeak voice";
        string sample = $"This is {voiceName}. SafeSpeak voice testing is working.";

        LiveStatusAnnouncement = $"Testing selected voice privately: {voiceName}";
        _voicePreviewOutput.Speak(sample, interrupt: true);
    }

    [RelayCommand]
    public async Task InstallKokoro()
    {
        if (IsDownloadingVoice) return;
        IsDownloadingVoice = true;
        VoiceDownloadProgress = 0;
        AnnounceState(IsKokoroInstalled ? "Kokoro is already installed." : "Installing Kokoro offline voices. This download is about 330 megabytes.");
        try
        {
            var progress = new Progress<double>(value => VoiceDownloadProgress = value);
            await _kokoroManager.InstallAsync(progress);
            LoadSystemAudioAndVoices();
            SelectedVoice = KokoroModelManager.VoicePrefix + "af_heart";
            OnPropertyChanged(nameof(IsKokoroInstalled));
            OnPropertyChanged(nameof(KokoroInstallationStatus));
            AnnounceState("Kokoro installed. Twenty seven offline neural voices are now available.");
        }
        catch (Exception ex)
        {
            AnnounceState($"Kokoro installation failed: {ex.Message}");
        }
        finally
        {
            IsDownloadingVoice = false;
        }
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
            AiClassificationEnabled = Config.AiClassificationEnabled,
            IsConnected = IsConnected,
            AnnounceChatMessages = AnnounceChatMessages,
            AnnounceGifts = AnnounceGifts,
            AnnounceFollows = AnnounceFollows,
            AnnounceShares = AnnounceShares,
            AnnounceSubscriptions = AnnounceSubscriptions,
            AnnounceJoins = AnnounceJoins,
            AnnounceLikes = AnnounceLikes,
            BroadcastOutputEnabled = BroadcastOutputEnabled,
            PrivateMonitorEnabled = PrivateMonitorEnabled,
            UseHighContrastTheme = UseHighContrastTheme
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

                case "toggle_connection":
                    if (IsConnected) await DisconnectTikFinity(); else await ConnectTikFinity();
                    return IsConnected ? "Connected" : "Disconnected";

                case "toggle_chat": AnnounceChatMessages = !AnnounceChatMessages; return ToggleResult("Chat", AnnounceChatMessages);
                case "toggle_gifts": AnnounceGifts = !AnnounceGifts; return ToggleResult("Gifts", AnnounceGifts);
                case "toggle_follows": AnnounceFollows = !AnnounceFollows; return ToggleResult("Follows", AnnounceFollows);
                case "toggle_shares": AnnounceShares = !AnnounceShares; return ToggleResult("Shares", AnnounceShares);
                case "toggle_subscriptions": AnnounceSubscriptions = !AnnounceSubscriptions; return ToggleResult("Subscriptions", AnnounceSubscriptions);
                case "toggle_joins": AnnounceJoins = !AnnounceJoins; return ToggleResult("Joins", AnnounceJoins);
                case "toggle_likes": AnnounceLikes = !AnnounceLikes; return ToggleResult("Likes", AnnounceLikes);
                case "toggle_broadcast_output": BroadcastOutputEnabled = !BroadcastOutputEnabled; return ToggleResult("BroadcastOutput", BroadcastOutputEnabled);
                case "toggle_private_monitor": PrivateMonitorEnabled = !PrivateMonitorEnabled; return ToggleResult("PrivateMonitor", PrivateMonitorEnabled);
                case "toggle_high_contrast": UseHighContrastTheme = !UseHighContrastTheme; return ToggleResult("HighContrast", UseHighContrastTheme);

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

    private string ToggleResult(string setting, bool enabled)
    {
        AnnounceState($"{setting} {(enabled ? "enabled" : "disabled")}");
        return setting + (enabled ? "Enabled" : "Disabled");
    }

    public async ValueTask DisposeAsync()
    {
        SaveSettings();
        _ipcServer.Dispose();
        await _tikFinityConnector.DisposeAsync();
        await _simulator.DisposeAsync();
        await _voicePreviewOutput.DisposeAsync();
        await _ttsQueue.DisposeAsync();
        _ttsEngine.Dispose();
        _audioRouter.Dispose();
        _privateAudioRouter.Dispose();
        _voicePreviewAudioRouter.Dispose();
        _announcer.Dispose();
        _privateAnnouncementLock.Dispose();
    }

    private void SaveSettings()
    {
        _settings.CaptureModerationConfig(Config);
        _settings.SelectedAudioEndpointId = string.IsNullOrEmpty(SelectedAudioEndpoint) ? null : SelectedAudioEndpoint;
        _settings.SelectedBroadcastEndpointId = string.IsNullOrEmpty(SelectedAudioEndpoint) ? null : SelectedAudioEndpoint;
        _settings.SelectedPrivateEndpointId = string.IsNullOrEmpty(SelectedPrivateAudioEndpoint) ? null : SelectedPrivateAudioEndpoint;
        _settings.SelectedVoiceName = string.IsNullOrEmpty(SelectedVoice) ? null : SelectedVoice;
        _settings.SpeechRate = Math.Clamp(SpeechRate, -5, 5);
        _settings.SpeechVolume = Math.Clamp(SpeechVolume, 0, 100);
        _settings.ReaderSpeechRate = Math.Clamp(ReaderSpeechRate, -5, 5);
        _settings.Save();
    }

    private void PersistModerationSettings()
    {
        _settings.CaptureModerationConfig(Config);
        _settings.Save();
        OnPropertyChanged(nameof(Config));
    }
}
