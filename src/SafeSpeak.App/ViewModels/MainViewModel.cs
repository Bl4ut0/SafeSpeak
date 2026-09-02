using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeSpeak.Core.AI;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Ipc;
using SafeSpeak.Core.Logging;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly record struct QueuedLivestreamEvent(
        LivestreamEvent Event,
        int MonitoringGeneration);

    private readonly ModerationPipeline _pipeline;
    private readonly ModerationTestService _moderationTestService;
    private readonly ISourceConnector _sourceConnector;
    private readonly ITtsEngine _ttsEngine;
    private readonly IAudioRouter _audioRouter;
    private readonly IAudioRouter _voicePreviewAudioRouter;
    private readonly PrivateVoicePreviewOutput _voicePreviewOutput;
    private readonly StreamAuditLogger _auditLogger;
    private readonly TtsQueue _ttsQueue;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly StreamDeckIpcServer _ipcServer;
    private readonly AppSettings _settings;
    private readonly Channel<QueuedLivestreamEvent> _incomingEvents =
        Channel.CreateBounded<QueuedLivestreamEvent>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    private readonly CancellationTokenSource _incomingEventCts = new();
    private readonly ConcurrentDictionary<string, byte> _sessionDonors =
        new(StringComparer.OrdinalIgnoreCase);
    private Task _incomingEventPumpTask = Task.CompletedTask;
    private int _droppedIncomingEventCount;
    private int _monitoringGeneration;
    private Task _autoConnectTask = Task.CompletedTask;
    private readonly object _disposeLock = new();
    private Task? _disposeTask;
    private bool _isInitializing = true;

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
    private int _readerSpeechRate = 3;

    [ObservableProperty]
    private string _customBlockedInput = "";

    [ObservableProperty]
    private string _liveStatusAnnouncement = "";

    [ObservableProperty]
    private bool _isDownloadingVoice = false;

    [ObservableProperty]
    private double _voiceDownloadProgress = 0;

    [ObservableProperty]
    private bool _broadcastOutputEnabled = true;

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
    private bool _englishOnly = true;

    [ObservableProperty]
    private bool _rejectMixedScripts = true;

    [ObservableProperty]
    private AudienceMode _selectedAudienceMode = AudienceMode.All;

    [ObservableProperty]
    private bool _allowDonorsToSpeak = true;

    [ObservableProperty]
    private bool _pauseAllTtsWhilePaused = true;

    [ObservableProperty]
    private bool _allowGiftAnnouncementsWhilePaused = true;

    [ObservableProperty]
    private bool _allowFollowAnnouncementsWhilePaused = true;

    [ObservableProperty]
    private bool _allowShareAnnouncementsWhilePaused = true;

    [ObservableProperty]
    private bool _allowSubscriptionAnnouncementsWhilePaused = true;

    [ObservableProperty]
    private bool _spokenGuidanceEnabled;

    [ObservableProperty]
    private ThemePreference _selectedTheme = ThemePreference.Light;

    [ObservableProperty]
    private bool _enableStreamAuditLogging;

    [ObservableProperty]
    private int _moderationLevel = 3;

    [ObservableProperty]
    private string? _selectedCustomBlockedTerm;

    [ObservableProperty]
    private string _filterTestInput = string.Empty;

    [ObservableProperty]
    private string _filterTestResult = "No filter test has been run.";

    [ObservableProperty]
    private bool _filterTestPassed;

    [ObservableProperty]
    private bool _hasFilterTestResult;

    public ObservableCollection<ModerationDecision> LiveFeed { get; } = new();
    public ObservableCollection<AudioEndpointInfo> AudioEndpoints { get; } = new();
    public ObservableCollection<VoiceInfo> Voices { get; } = new();
    public ObservableCollection<string> CustomBlockedTerms { get; } = new();
    public IReadOnlyList<ThemeChoice> ThemeChoices { get; } =
    [
        new(ThemePreference.Light, "Light", 1),
        new(ThemePreference.Dark, "Dark", 2),
        new(ThemePreference.HighContrast, "High Contrast", 3)
    ];
    public IReadOnlyList<AudienceChoice> AudienceChoices { get; } =
    [
        new(AudienceMode.All, "Everyone", 1),
        new(AudienceMode.FollowersOnly, "Followers", 2),
        new(AudienceMode.SubscribersOnly, "Subscribers", 3),
        new(AudienceMode.ModeratorsOnly, "Moderators", 4)
    ];
    public bool IsKokoroInstalled => _kokoroManager.IsInstalled;
    public bool ShowKokoroInstallAction => !IsKokoroInstalled;
    public string KokoroInstallationStatus => IsKokoroInstalled
        ? $"Installed. {KokoroModelManager.EnglishVoices.Count} offline neural voices are available."
        : "Optional. Installs one local model with 27 English voices; speech stays on this computer.";

    public ModerationConfig Config => _pipeline.Config;
    public string AuditLogsDirectoryDisplay => _auditLogger.LogsDirectory;
    public string BannedRulesSummary =>
        $"{_pipeline.Rules.DefaultRules.Count + CustomBlockedTerms.Count} active terms";
    public string EvasionRulesSummary => "Unicode and spacing protection active";
    public ScreenReaderAnnouncer Announcer => _announcer;
    public string SpokenGuidanceStatus => !SpokenGuidanceEnabled
        ? "SafeSpeak spoken guidance is disabled; Windows Narrator and other UI Automation readers remain supported"
        : _announcer.IsSpeechAvailable
            ? "SafeSpeak spoken guidance is enabled and Windows speech is ready"
            : "SafeSpeak spoken guidance is enabled, but Windows speech is unavailable; Windows Narrator and other UI Automation readers remain supported";
    public string ThemeStatus => $"{GetThemeDisplayName(SelectedTheme)} theme selected";
    public string ThemeSelectionAccessibleText
    {
        get
        {
            int index = ThemeChoices
                .Select((choice, position) => (choice, position))
                .First(item => item.choice.Value == SelectedTheme)
                .position;
            return
                $"Current selection: {GetThemeDisplayName(SelectedTheme)}, option {index + 1} of {ThemeChoices.Count}";
        }
    }
    public string AudienceSelectionAccessibleText
    {
        get
        {
            int index = AudienceChoices
                .Select((choice, position) => (choice, position))
                .First(item => item.choice.Value == SelectedAudienceMode)
                .position;
            return
                $"Current audience: {AudienceChoices[index].DisplayName}, option {index + 1} of {AudienceChoices.Count}";
        }
    }
    public string RequiredSafetyFeaturesStatus =>
        "Intent moderation is always active. Viewer names are always moderated and included before chat messages.";
    public string PauseRoutingSummary
    {
        get
        {
            if (PauseAllTtsWhilePaused)
            {
                return "Pause holds all text to speech, including event announcements.";
            }

            var bypassed = new List<string>(4);
            if (AllowGiftAnnouncementsWhilePaused) bypassed.Add("gifts");
            if (AllowFollowAnnouncementsWhilePaused) bypassed.Add("follows");
            if (AllowShareAnnouncementsWhilePaused) bypassed.Add("shares");
            if (AllowSubscriptionAnnouncementsWhilePaused) bypassed.Add("subscriptions");
            return bypassed.Count == 0
                ? "Pause currently holds all text to speech because no event bypasses are selected."
                : $"While chat is paused, {string.Join(", ", bypassed)} can still be spoken. Emergency Stop always stops everything.";
        }
    }
    public string SourceName => _sourceConnector.Descriptor.DisplayName;
    public string SourceDescription => _sourceConnector.Descriptor.ProviderName;
    public string IntentModelStatus => _pipeline.Classifier is LocalOnnxIntentClassifier local
        ? local.IsModelLoaded
            ? "Enhanced filtering model: bundled, installed, and active on this computer."
            : $"Enhanced filtering model: unavailable. Deterministic local filtering remains active. {local.AvailabilityMessage}"
        : $"Selected intent moderation engine: {_pipeline.Classifier.ModelName}. The bundled local model remains packaged as its fallback.";
    public string IntentModelShortStatus => _pipeline.Classifier is LocalOnnxIntentClassifier local
        ? local.IsModelLoaded
            ? "Enhanced model — Active"
            : "Local fallback — Active"
        : $"{_pipeline.Classifier.ModelName} — Active";
    public string ModerationLevelName => Math.Clamp(ModerationLevel, 1, 4) switch
    {
        1 => "Relaxed",
        2 => "Balanced",
        3 => "Strong",
        4 => "Maximum",
        _ => "Strong"
    };
    public string ModerationLevelDescription => Math.Clamp(ModerationLevel, 1, 4) switch
    {
        1 => "Blocks banned terms and clear threats; allows more uncertain language.",
        2 => "Also blocks strong harassment while allowing isolated low-confidence language.",
        3 => "Blocks directed insults and harassment. Recommended for most streams.",
        4 => "Blocks lower-confidence hostile phrasing as well as all stronger signals.",
        _ => "Blocks directed insults and harassment. Recommended for most streams."
    };
    public string ModerationLevelAccessibleText =>
        $"Moderation strength selector: {ModerationLevelName}, level {Math.Clamp(ModerationLevel, 1, 4)} of 4. {ModerationLevelDescription}";
    public string ModerationStrengthSummary =>
        $"{ModerationLevelName} ({Math.Clamp(ModerationLevel, 1, 4)} of 4)";

    public sealed record ThemeChoice(
        ThemePreference Value,
        string DisplayName,
        int Position);

    public sealed record AudienceChoice(
        AudienceMode Value,
        string DisplayName,
        int Position);

    private readonly KokoroModelManager _kokoroManager;

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _pipeline = new ModerationPipeline(_settings.CreateModerationConfig());
        _moderationTestService = new ModerationTestService(_pipeline);
        var connectorRegistry = SourceConnectorRegistry.CreateDefault();
        string connectorId = connectorRegistry.Descriptors.Any(
            descriptor => string.Equals(
                descriptor.Id,
                _settings.SelectedSourceConnectorId,
                StringComparison.OrdinalIgnoreCase))
            ? _settings.SelectedSourceConnectorId
            : TikFinityWebSocketClient.ConnectorDescriptor.Id;
        _sourceConnector = connectorRegistry.Create(connectorId);
        _settings.SelectedSourceConnectorId = _sourceConnector.Descriptor.Id;
        _kokoroManager = new KokoroModelManager();
        _ttsEngine = new ModularTtsEngine(_kokoroManager);
        _audioRouter = new WasapiAudioRouter();
        _voicePreviewAudioRouter = new WasapiAudioRouter();
        _ttsQueue = new TtsQueue(_ttsEngine, _audioRouter);
        _voicePreviewOutput = new PrivateVoicePreviewOutput(_ttsEngine, _voicePreviewAudioRouter);
        _auditLogger = new StreamAuditLogger();
        _auditLogger.IsEnabled = _settings.EnableStreamAuditLogging;
        EnableStreamAuditLogging = _settings.EnableStreamAuditLogging;
        SelectedAudioEndpoint = _settings.SelectedBroadcastEndpointId ?? _settings.SelectedAudioEndpointId ?? string.Empty;
        SelectedVoice = _settings.SelectedVoiceName ?? string.Empty;
        SpeechRate = Math.Clamp(_settings.SpeechRate, -5, 5);
        SpeechVolume = Math.Clamp(_settings.SpeechVolume, 0, 100);
        ReaderSpeechRate = Math.Clamp(_settings.ReaderSpeechRate, -5, 5);
        BroadcastOutputEnabled = _settings.BroadcastOutputEnabled;
        AnnounceChatMessages = _settings.AnnounceChatMessages;
        AnnounceGifts = _settings.AnnounceGifts;
        AnnounceFollows = _settings.AnnounceFollows;
        AnnounceShares = _settings.AnnounceShares;
        AnnounceSubscriptions = _settings.AnnounceSubscriptions;
        AnnounceJoins = _settings.AnnounceJoins;
        AnnounceLikes = _settings.AnnounceLikes;
        EnglishOnly = Config.EnglishOnly;
        RejectMixedScripts = Config.RejectMixedScripts;
        SelectedAudienceMode = Config.AudienceMode;
        AllowDonorsToSpeak = Config.AllowDonorsToSpeak;
        PauseAllTtsWhilePaused = _settings.PauseAllTtsWhilePaused;
        AllowGiftAnnouncementsWhilePaused = _settings.AllowGiftAnnouncementsWhilePaused;
        AllowFollowAnnouncementsWhilePaused = _settings.AllowFollowAnnouncementsWhilePaused;
        AllowShareAnnouncementsWhilePaused = _settings.AllowShareAnnouncementsWhilePaused;
        AllowSubscriptionAnnouncementsWhilePaused = _settings.AllowSubscriptionAnnouncementsWhilePaused;
        ModerationLevel = Math.Clamp(Config.IntentModerationLevel, 1, 4);
        foreach (string term in Config.CustomBlockedTerms
                     .Where(term => !string.IsNullOrWhiteSpace(term))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(term => term, StringComparer.CurrentCultureIgnoreCase))
        {
            CustomBlockedTerms.Add(term);
        }
        _announcer = new ScreenReaderAnnouncer();
        RefreshAccessibilitySettingsFromStore();
        _announcer.SpeechRate = ReaderSpeechRate;

        _ipcServer = new StreamDeckIpcServer(
            stateProvider: GetIpcState,
            commandHandler: HandleIpcCommandAsync
        );

        _incomingEventPumpTask = ProcessIncomingEventsAsync(_incomingEventCts.Token);
        WireEvents();
        LoadSystemAudioAndVoices();

        _ipcServer.Start();
        _isInitializing = false;
        _autoConnectTask = AutoConnectSourceAsync();
    }

    private void WireEvents()
    {
        _sourceConnector.EventReceived += SourceConnector_EventReceived;
        _sourceConnector.StateChanged += SourceConnector_StateChanged;
        _ttsQueue.StateChanged += TtsQueue_StateChanged;
        _ttsQueue.PlaybackStarted += TtsQueue_PlaybackStarted;
        _ttsQueue.PlaybackFinished += TtsQueue_PlaybackFinished;
    }

    private void SourceConnector_StateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        if (_incomingEventCts.IsCancellationRequested) return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_incomingEventCts.IsCancellationRequested) return;

            IsConnected = e.State == ConnectionState.Connected;
            ConnectionStatusText = $"{SourceName}: {e.State}";
            if (IsConnected)
            {
                _auditLogger.StartSession(SourceName, _sourceConnector.Descriptor.ProviderName, Config);
                _announcer.PlayCue(SoundCueType.TikFinityConnected);
                AnnounceState($"{SourceName} connected");
            }
            else
            {
                _auditLogger.EndSession();
                if (e.State == ConnectionState.Disconnected)
                {
                    _announcer.PlayCue(SoundCueType.TikFinityDisconnected);
                    AnnounceState($"{SourceName} disconnected");
                }
                else if (e.State == ConnectionState.Reconnecting)
                {
                    LiveStatusAnnouncement = $"{SourceName} is unavailable. SafeSpeak will keep trying automatically.";
                }
            }
        });
    }

    private void SourceConnector_EventReceived(object? sender, LivestreamEvent liveEvent) =>
        QueueIncomingEvent(liveEvent);

    private void QueueIncomingEvent(LivestreamEvent liveEvent)
    {
        if (_incomingEventCts.IsCancellationRequested) return;
        int generation = Volatile.Read(ref _monitoringGeneration);
        if (!_ttsQueue.IsArmed ||
            generation != Volatile.Read(ref _monitoringGeneration))
        {
            return;
        }

        if (_incomingEvents.Writer.TryWrite(
                new QueuedLivestreamEvent(liveEvent, generation)))
        {
            return;
        }

        if (Interlocked.Increment(ref _droppedIncomingEventCount) == 1)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
                AnnounceState(
                    "The incoming event buffer is full. New events are being dropped to keep SafeSpeak responsive."));
        }
    }

    private async Task ProcessIncomingEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (QueuedLivestreamEvent queuedEvent in
                           _incomingEvents.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await HandleIncomingEventAsync(
                        queuedEvent.Event,
                        queuedEvent.MonitoringGeneration,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                        AnnounceState($"A {SourceName} event could not be processed. {ex.Message}"));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown: do not process stale buffered events after close.
        }
    }

    private async Task AutoConnectSourceAsync()
    {
        if (!_settings.AutoConnectSource) return;

        ConnectionStatusText = $"{SourceName}: Connecting";
        try
        {
            await _sourceConnector.ConnectAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatusText = $"{SourceName}: Connection failed";
            AnnounceState($"{SourceName} could not start. {ex.Message}");
        }
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
        _ttsQueue.BroadcastOutputEnabled = BroadcastOutputEnabled;
        UpdateVoicePreviewSettings();
        UpdateVoicePreviewAudioEndpoint();
    }

    partial void OnSelectedAudioEndpointChanged(string value)
    {
        _audioRouter?.SelectEndpoint(string.IsNullOrEmpty(value) ? null : value);
        if (_isInitializing) return;
        _settings.SelectedAudioEndpointId = string.IsNullOrEmpty(value) ? null : value;
        _settings.SelectedBroadcastEndpointId = string.IsNullOrEmpty(value) ? null : value;
        SaveSettingsOrReport();
        AudioEndpointInfo? endpoint = AudioEndpoints.FirstOrDefault(
            candidate => string.Equals(candidate.Id, value, StringComparison.Ordinal));
        AnnounceOptionSelection(
            "Broadcast audio device",
            endpoint?.Name,
            endpoint is null ? -1 : AudioEndpoints.IndexOf(endpoint),
            AudioEndpoints.Count);
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
        SaveSettingsOrReport();

        VoiceInfo? voice = Voices.FirstOrDefault(
            candidate => string.Equals(candidate.Id, value, StringComparison.Ordinal));
        AnnounceOptionSelection(
            "Voice",
            voice?.DisplayName,
            voice is null ? -1 : Voices.IndexOf(voice),
            Voices.Count);
    }

    private void AnnounceOptionSelection(
        string category,
        string? optionName,
        int zeroBasedIndex,
        int optionCount)
    {
        if (!_announcer.IsEnhancedAccessibilityEnabled ||
            string.IsNullOrWhiteSpace(optionName))
        {
            return;
        }

        string position = zeroBasedIndex >= 0 && optionCount > 0
            ? $", {zeroBasedIndex + 1} of {optionCount}"
            : string.Empty;
        _announcer.Announce($"{category}: {optionName}{position}.");
    }

    partial void OnEnableStreamAuditLoggingChanged(bool value)
    {
        if (_auditLogger is not null)
        {
            _auditLogger.IsEnabled = value;
            if (value && IsConnected)
            {
                _auditLogger.StartSession(SourceName, _sourceConnector.Descriptor.ProviderName, Config);
            }
        }
        if (_isInitializing) return;
        _settings.EnableStreamAuditLogging = value;
        SaveSettingsOrReport();
        AnnounceState(value ? "Stream audit logging enabled. Unfiltered chat is saved to disk." : "Stream audit logging disabled.");
    }

    partial void OnSpeechRateChanged(int value)
    {
        if (_ttsQueue is not null) _ttsQueue.SpeechRate = Math.Clamp(value, -5, 5);
        UpdateVoicePreviewSettings();
        if (_isInitializing) return;
        _settings.SpeechRate = Math.Clamp(value, -5, 5);
        SaveSettingsOrReport();
    }

    partial void OnSpeechVolumeChanged(int value)
    {
        if (_ttsQueue is not null) _ttsQueue.SpeechVolume = Math.Clamp(value, 0, 100);
        UpdateVoicePreviewSettings();
        if (_isInitializing) return;
        _settings.SpeechVolume = Math.Clamp(value, 0, 100);
        SaveSettingsOrReport();
    }

    partial void OnReaderSpeechRateChanged(int value)
    {
        if (_announcer is not null) _announcer.SpeechRate = Math.Clamp(value, -5, 5);
        if (_isInitializing) return;
        _settings.ReaderSpeechRate = Math.Clamp(value, -5, 5);
        SaveSettingsOrReport();
    }

    partial void OnBroadcastOutputEnabledChanged(bool value) => SaveOutputSettings();
    partial void OnAnnounceChatMessagesChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceGiftsChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceFollowsChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceSharesChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceSubscriptionsChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceJoinsChanged(bool value) => SaveEventSettings();
    partial void OnAnnounceLikesChanged(bool value) => SaveEventSettings();
    partial void OnEnglishOnlyChanged(bool value)
    {
        if (_pipeline is null) return;
        Config.EnglishOnly = value;
        if (_isInitializing) return;
        PersistModerationSettings();
        AnnounceState(value
            ? "English and Latin-script filtering enabled."
            : "English and Latin-script filtering disabled.");
    }

    partial void OnRejectMixedScriptsChanged(bool value)
    {
        if (_pipeline is null) return;
        Config.RejectMixedScripts = value;
        if (_isInitializing) return;
        PersistModerationSettings();
        AnnounceState(value
            ? "Mixed-script evasion protection enabled."
            : "Mixed-script evasion protection disabled.");
    }

    partial void OnSelectedAudienceModeChanged(AudienceMode value)
    {
        if (_pipeline is null) return;
        Config.AudienceMode = value;
        OnPropertyChanged(nameof(AudienceSelectionAccessibleText));
        if (_isInitializing) return;
        PersistModerationSettings();
        string displayName = AudienceChoices
            .First(choice => choice.Value == value)
            .DisplayName;
        AnnounceState($"Chat audience changed to {displayName}.");
    }

    partial void OnAllowDonorsToSpeakChanged(bool value)
    {
        if (_pipeline is null) return;
        Config.AllowDonorsToSpeak = value;
        if (_isInitializing) return;
        PersistModerationSettings();
        AnnounceState(value
            ? "Gift senders can speak even when the selected audience would otherwise exclude them."
            : "Gift senders must now meet the selected chat audience requirement.");
    }

    partial void OnPauseAllTtsWhilePausedChanged(bool value) =>
        PauseRoutingSettingChanged();
    partial void OnAllowGiftAnnouncementsWhilePausedChanged(bool value) =>
        PauseRoutingSettingChanged();
    partial void OnAllowFollowAnnouncementsWhilePausedChanged(bool value) =>
        PauseRoutingSettingChanged();
    partial void OnAllowShareAnnouncementsWhilePausedChanged(bool value) =>
        PauseRoutingSettingChanged();
    partial void OnAllowSubscriptionAnnouncementsWhilePausedChanged(bool value) =>
        PauseRoutingSettingChanged();

    private void PauseRoutingSettingChanged()
    {
        OnPropertyChanged(nameof(PauseRoutingSummary));
        SaveEventSettings();
        if (!_isInitializing)
        {
            AnnounceState(PauseRoutingSummary);
        }
    }
    partial void OnSpokenGuidanceEnabledChanged(bool value)
    {
        _announcer.IsEnhancedAccessibilityEnabled = value;
        OnPropertyChanged(nameof(SpokenGuidanceStatus));
        if (_isInitializing) return;

        _settings.SpokenGuidance = value
            ? SpokenGuidanceMode.Enabled
            : SpokenGuidanceMode.Disabled;
        _settings.PendingSpokenGuidance = SpokenGuidanceMode.Unset;
        SaveSettingsOrReport();
        AnnounceState(value
            ? "SafeSpeak spoken guidance enabled."
            : "SafeSpeak spoken guidance disabled. Windows screen readers remain supported.");
    }

    partial void OnSelectedThemeChanged(ThemePreference value)
    {
        ThemePreference normalized = NormalizeTheme(value);
        if (value != normalized)
        {
            SelectedTheme = normalized;
            return;
        }

        ThemeManager.Apply(normalized);
        OnPropertyChanged(nameof(ThemeStatus));
        OnPropertyChanged(nameof(ThemeSelectionAccessibleText));
        if (_isInitializing) return;

        _settings.Theme = normalized;
        _settings.PendingTheme = ThemePreference.Unset;
        SaveSettingsOrReport();
        AnnounceState($"{GetThemeDisplayName(normalized)} theme selected and applied.");
    }

    private void RefreshAccessibilitySettingsFromStore()
    {
        SelectedTheme = NormalizeTheme(_settings.EffectiveTheme);
        SpokenGuidanceEnabled = _settings.IsSpokenGuidanceEnabled;
        ThemeManager.Apply(SelectedTheme);
        _announcer.IsEnhancedAccessibilityEnabled = SpokenGuidanceEnabled;
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(ThemeStatus));
        OnPropertyChanged(nameof(ThemeSelectionAccessibleText));
        OnPropertyChanged(nameof(SpokenGuidanceEnabled));
        OnPropertyChanged(nameof(SpokenGuidanceStatus));
    }

    private static ThemePreference NormalizeTheme(ThemePreference theme) => theme switch
    {
        ThemePreference.Dark => ThemePreference.Dark,
        ThemePreference.HighContrast => ThemePreference.HighContrast,
        _ => ThemePreference.Light
    };

    private static string GetThemeDisplayName(ThemePreference theme) => theme switch
    {
        ThemePreference.Dark => "Dark",
        ThemePreference.HighContrast => "High Contrast",
        _ => "Light"
    };

    partial void OnModerationLevelChanged(int value)
    {
        if (_pipeline is null) return;
        Config.IntentModerationLevel = Math.Clamp(value, 1, 4);
        Config.AiClassificationEnabled = true;
        OnPropertyChanged(nameof(ModerationLevelName));
        OnPropertyChanged(nameof(ModerationLevelDescription));
        OnPropertyChanged(nameof(ModerationLevelAccessibleText));
        OnPropertyChanged(nameof(ModerationStrengthSummary));
        if (_isInitializing) return;
        PersistModerationSettings();
        AnnounceState($"Moderation strength changed to {ModerationLevelName}, level {Config.IntentModerationLevel} of 4.");
    }

    private void SaveOutputSettings()
    {
        if (_ttsQueue is null) return;
        _ttsQueue.BroadcastOutputEnabled = BroadcastOutputEnabled;
        if (_isInitializing) return;
        _settings.BroadcastOutputEnabled = BroadcastOutputEnabled;
        SaveSettingsOrReport();
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

        string? endpointId = AudioEndpoints.FirstOrDefault(endpoint => endpoint.IsDefault)?.Id;
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
        _settings.PauseAllTtsWhilePaused = PauseAllTtsWhilePaused;
        _settings.AllowGiftAnnouncementsWhilePaused = AllowGiftAnnouncementsWhilePaused;
        _settings.AllowFollowAnnouncementsWhilePaused = AllowFollowAnnouncementsWhilePaused;
        _settings.AllowShareAnnouncementsWhilePaused = AllowShareAnnouncementsWhilePaused;
        _settings.AllowSubscriptionAnnouncementsWhilePaused = AllowSubscriptionAnnouncementsWhilePaused;
        SaveSettingsOrReport();
    }

    private async Task HandleIncomingEventAsync(
        LivestreamEvent liveEvent,
        int monitoringGeneration,
        CancellationToken cancellationToken)
    {
        if (!IsMonitoringGenerationActive(monitoringGeneration))
        {
            return;
        }

        if (liveEvent.Type == LivestreamEventType.Gift)
        {
            TrackSessionDonor(liveEvent.Author, liveEvent.AuthorDisplayName);
        }

        if (liveEvent.Type == LivestreamEventType.Chat)
        {
            if (AnnounceChatMessages)
            {
                ChatMessage chatMessage = liveEvent.ToChatMessage();
                if (IsSessionDonor(chatMessage.Author, chatMessage.AuthorDisplayName))
                {
                    chatMessage = chatMessage with { IsDonor = true };
                }
                await HandleIncomingMessageAsync(
                    chatMessage,
                    monitoringGeneration,
                    cancellationToken);
            }
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
        if (!enabled)
        {
            if (IsMonitoringGenerationActive(monitoringGeneration))
            {
                _auditLogger.LogEvent(liveEvent, null);
            }
            return;
        }

        string spoken = liveEvent.Type switch
        {
            LivestreamEventType.Gift => $"sent {liveEvent.GiftCount} {liveEvent.GiftName} gift{(liveEvent.GiftCount == 1 ? "" : "s")}",
            LivestreamEventType.Follow => "followed the stream",
            LivestreamEventType.Share => "shared the stream",
            LivestreamEventType.Subscribe => "subscribed",
            LivestreamEventType.Join => "joined",
            LivestreamEventType.Like => "liked the stream",
            _ => string.Empty
        };
        bool bypassPause = !PauseAllTtsWhilePaused && liveEvent.Type switch
        {
            LivestreamEventType.Gift => AllowGiftAnnouncementsWhilePaused,
            LivestreamEventType.Follow => AllowFollowAnnouncementsWhilePaused,
            LivestreamEventType.Share => AllowShareAnnouncementsWhilePaused,
            LivestreamEventType.Subscribe => AllowSubscriptionAnnouncementsWhilePaused,
            _ => false
        };
        await HandleIncomingMessageAsync(
            new ChatMessage
            {
                Author = liveEvent.Author,
                AuthorDisplayName = liveEvent.AuthorDisplayName,
                RawText = spoken,
                AttributionStyle = SpokenAttributionStyle.LeadingName,
                AuthorTier = liveEvent.AuthorTier,
                IsSubscriber = liveEvent.IsSubscriber,
                IsModerator = liveEvent.IsModerator,
                IsDonor = liveEvent.Type == LivestreamEventType.Gift ||
                    IsSessionDonor(liveEvent.Author, liveEvent.AuthorDisplayName)
            },
            monitoringGeneration,
            cancellationToken,
            originalEvent: liveEvent,
            bypassPause: bypassPause);
    }

    private async Task HandleIncomingMessageAsync(
        ChatMessage message,
        int monitoringGeneration,
        CancellationToken cancellationToken,
        LivestreamEvent? originalEvent = null,
        bool bypassPause = false)
    {
        if (!IsMonitoringGenerationActive(monitoringGeneration))
        {
            return;
        }

        var decision = await _pipeline.ProcessMessageAsync(message, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsMonitoringGenerationActive(monitoringGeneration))
        {
            return;
        }

        if (originalEvent != null)
        {
            _auditLogger.LogEvent(originalEvent, decision);
        }
        else
        {
            _auditLogger.LogDecision(message, decision);
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (LiveFeed.Count >= 100)
            {
                LiveFeed.RemoveAt(LiveFeed.Count - 1);
            }
            LiveFeed.Insert(0, decision);

            if (decision.Passed && IsArmed)
            {
                if (!_ttsQueue.Enqueue(decision, bypassPause))
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

    private bool IsMonitoringGenerationActive(int monitoringGeneration) =>
        _ttsQueue.IsArmed &&
        monitoringGeneration == Volatile.Read(ref _monitoringGeneration);

    private void TrackSessionDonor(string author, string displayName)
    {
        string key = DonorKey(author, displayName);
        if (key.Length > 0)
        {
            _sessionDonors.TryAdd(key, 0);
        }
    }

    private bool IsSessionDonor(string author, string displayName)
    {
        string key = DonorKey(author, displayName);
        return key.Length > 0 && _sessionDonors.ContainsKey(key);
    }

    private static string DonorKey(string author, string displayName) =>
        (string.IsNullOrWhiteSpace(author) ? displayName : author).Trim();

    [RelayCommand]
    public async Task ConnectTikFinity()
    {
        await _sourceConnector.ConnectAsync();
    }

    [RelayCommand]
    public async Task DisconnectTikFinity()
    {
        await _sourceConnector.DisconnectAsync();
    }

    [RelayCommand]
    public async Task RetrySourceConnection()
    {
        await _sourceConnector.DisconnectAsync();
        await _sourceConnector.ConnectAsync();
        AnnounceState($"Retrying {SourceName} connection.");
    }

    [RelayCommand]
    public void AnnounceStatusPrivately()
    {
        string armedStr = IsArmed
            ? $"Armed in {PlaybackModeStatus} mode"
            : "Disarmed";
        string queueStr = QueueCount == 1
            ? "1 message in queue"
            : $"{QueueCount} messages in queue";
        string speechStr = IsSpeaking
            ? "A message is speaking now"
            : "Speech is idle";
        string broadcastRoute = BroadcastOutputEnabled
            ? $"Broadcast output: {AudioEndpointFormatter.GetFriendlyName(AudioEndpoints, SelectedAudioEndpoint)}"
            : "Broadcast output disabled";
        string announcement =
            $"SafeSpeak {armedStr}. Source status: {ConnectionStatusText}. " +
            $"{queueStr}. {speechStr}. {broadcastRoute}.";
        AnnounceState(announcement, interrupt: true);
    }

    [RelayCommand]
    public void AddCustomBlockedTerm()
    {
        string term = CustomBlockedInput.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            AnnounceState("Type a word or phrase before adding a banned term.");
            return;
        }

        if (CustomBlockedTerms.Any(existing =>
                string.Equals(existing, term, StringComparison.OrdinalIgnoreCase)))
        {
            AnnounceState("That banned term is already in the list.");
            return;
        }

        CustomBlockedTerms.Add(term);
        Config.CustomBlockedTerms.Add(term);
        PersistModerationSettings();
        SelectedCustomBlockedTerm = term;
        OnPropertyChanged(nameof(BannedRulesSummary));
        AnnounceState($"Added banned term: {term}");
        CustomBlockedInput = "";
    }

    [RelayCommand]
    public void RemoveSelectedCustomBlockedTerm()
    {
        if (string.IsNullOrWhiteSpace(SelectedCustomBlockedTerm))
        {
            AnnounceState("Choose a banned term to remove.");
            return;
        }

        string term = SelectedCustomBlockedTerm;
        CustomBlockedTerms.Remove(term);
        Config.CustomBlockedTerms.RemoveAll(existing =>
            string.Equals(existing, term, StringComparison.OrdinalIgnoreCase));
        SelectedCustomBlockedTerm = null;
        PersistModerationSettings();
        OnPropertyChanged(nameof(BannedRulesSummary));
        AnnounceState($"Removed banned term: {term}");
    }

    [RelayCommand]
    public async Task TestFilter()
    {
        string sample = FilterTestInput.Trim();
        if (string.IsNullOrWhiteSpace(sample))
        {
            FilterTestPassed = false;
            FilterTestResult = "Enter a message before testing the filter.";
            HasFilterTestResult = true;
            AnnounceState(FilterTestResult);
            return;
        }

        try
        {
            ModerationTestResult result = await _moderationTestService.EvaluateAsync(sample);

            FilterTestPassed = result.IsAllowed;
            FilterTestResult = result.AccessibleSummary;
            HasFilterTestResult = true;
            AnnounceState($"Filter test result. {FilterTestResult}");
        }
        catch (Exception)
        {
            FilterTestPassed = false;
            FilterTestResult = "Filter test unavailable — the current moderation engine could not complete the check.";
            HasFilterTestResult = true;
            AnnounceState(FilterTestResult);
        }
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
    public void OpenAuditLogsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _auditLogger.LogsDirectory,
                UseShellExecute = true
            });
            AnnounceState("Opening SafeSpeak stream audit logs folder.");
        }
        catch
        {
            AnnounceState(
                $"SafeSpeak could not open the logs folder. Log folder: {_auditLogger.LogsDirectory}");
        }
    }

    [RelayCommand]
    public void RerunAccessibilityWizard()
    {
        Views.AccessibilitySetupDialog? wizard = null;
        var setupViewModel = new AccessibilitySetupViewModel(
            _settings,
            _announcer,
            onCompleted: () =>
            {
                RefreshAccessibilitySettingsFromStore();
                AnnounceState("Accessibility settings updated successfully.");
                wizard?.Close();
            },
            onRestartRequired: () => wizard?.Close(),
            changeExistingProfile: true);

        wizard = new Views.AccessibilitySetupDialog(setupViewModel)
        {
            Owner = Application.Current?.MainWindow
        };
        wizard.ShowDialog();
    }

    [RelayCommand]
    public async Task TestSelectedVoice()
    {
        VoiceInfo? voice = Voices.FirstOrDefault(
            candidate => string.Equals(candidate.Id, SelectedVoice, StringComparison.Ordinal));
        string voiceName = voice?.DisplayName ?? "the selected SafeSpeak voice";
        string sample = $"This is {voiceName}. SafeSpeak voice testing is working.";

        LiveStatusAnnouncement = $"Testing selected voice on the preview output: {voiceName}";
        try
        {
            await _voicePreviewOutput.SpeakAsync(sample, interrupt: true);
            LiveStatusAnnouncement = $"Voice preview completed: {voiceName}";
        }
        catch (OperationCanceledException)
        {
            // A newer preview or shutdown deliberately superseded this request.
        }
        catch (Exception ex)
        {
            AnnounceState($"Voice preview failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task InstallKokoro()
    {
        if (IsDownloadingVoice) return;
        if (IsKokoroInstalled)
        {
            AnnounceState(KokoroInstallationStatus);
            return;
        }

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
            OnPropertyChanged(nameof(ShowKokoroInstallAction));
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
            IsConnected = IsConnected
        };
    }

    private Task<string> HandleIpcCommandAsync(string command, string parameter)
    {
        return Application.Current.Dispatcher.Invoke(async () =>
        {
            switch (command.ToLowerInvariant())
            {
                case "arm":
                    ArmSafeSpeak();
                    return "Armed";

                case "disarm":
                    DisarmSafeSpeak();
                    return "Disarmed";

                case "toggle_arm":
                    ToggleArm();
                    return IsArmed ? "Armed" : "Disarmed";

                case "toggle_autoplay":
                    if (_ttsQueue.Mode == TtsPlaybackMode.Automatic)
                    {
                        UseManualPlayback();
                        return "ManualPlaybackEnabled";
                    }
                    UseAutomaticPlayback();
                    return "AutomaticPlaybackEnabled";

                case "toggle_pause":
                    PauseOrResumeTts();
                    return _ttsQueue.Mode == TtsPlaybackMode.Paused ? "Paused" : "Resumed";

                case "pause":
                    _ttsQueue.SetPaused(true);
                    AnnounceState("TTS Queue Paused");
                    return "Paused";

                case "resume":
                    _ttsQueue.ResumeAutomatic();
                    AnnounceState("TTS Queue Resumed");
                    return "Resumed";

                case "manual":
                    UseManualPlayback();
                    return "ManualPlaybackEnabled";

                case "automatic":
                    UseAutomaticPlayback();
                    return "AutomaticPlaybackEnabled";

                case "toggle_english":
                    EnglishOnly = !EnglishOnly;
                    return EnglishOnly ? "EnglishOnlyEnabled" : "EnglishOnlyDisabled";

                case "toggle_mixedscripts":
                    RejectMixedScripts = !RejectMixedScripts;
                    return RejectMixedScripts ? "MixedScriptsBlocked" : "MixedScriptsAllowed";

                case "toggle_usernames":
                    Config.SpeakUsernames = true;
                    AnnounceState("Moderated viewer names are always included with chat speech.");
                    return "UsernamesAlwaysEnabled";

                case "toggle_aiclassifier":
                    Config.AiClassificationEnabled = true;
                    AnnounceState("Intent moderation is always enabled. Use moderation strength to adjust it.");
                    return "IntentModerationAlwaysEnabled";

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
                case "toggle_high_contrast":
                    SelectedTheme = SelectedTheme == ThemePreference.HighContrast
                        ? ThemePreference.Light
                        : ThemePreference.HighContrast;
                    return SelectedTheme == ThemePreference.HighContrast
                        ? "HighContrastEnabled"
                        : "HighContrastDisabled";

                case "cycle_audience":
                    SelectedAudienceMode = SelectedAudienceMode switch
                    {
                        AudienceMode.All => AudienceMode.FollowersOnly,
                        AudienceMode.FollowersOnly => AudienceMode.SubscribersOnly,
                        AudienceMode.SubscribersOnly => AudienceMode.ModeratorsOnly,
                        AudienceMode.ModeratorsOnly => AudienceMode.All,
                        _ => AudienceMode.All
                    };
                    return SelectedAudienceMode.ToString();

                case "cycle_strictness":
                    ModerationLevel = ModerationLevel >= 4 ? 1 : ModerationLevel + 1;
                    return ModerationLevelName;

                case "stop_current":
                case "skip": // Temporary Stream Deck compatibility alias.
                    StopCurrentSpeech();
                    return "CurrentSpeechStopped";

                case "emergency_stop":
                case "panic": // Temporary Stream Deck compatibility alias.
                    EmergencyStop();
                    return "EmergencyStopExecuted";

                case "clear_queue":
                case "clear": // SafeSpeak Stream Deck 1.0 compatibility alias.
                    ClearQueue();
                    return "QueueCleared";

                case "speak_next":
                case "next":
                    await SpeakNextApprovedMessage();
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

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposeTask ??= BeginDispose();
            return new ValueTask(_disposeTask);
        }
    }

    private Task BeginDispose()
    {
        _incomingEvents.Writer.TryComplete();
        _incomingEventCts.Cancel();
        _sourceConnector.EventReceived -= SourceConnector_EventReceived;
        _sourceConnector.StateChanged -= SourceConnector_StateChanged;
        _ttsQueue.StateChanged -= TtsQueue_StateChanged;
        _ttsQueue.PlaybackStarted -= TtsQueue_PlaybackStarted;
        _ttsQueue.PlaybackFinished -= TtsQueue_PlaybackFinished;

        Interlocked.Increment(ref _monitoringGeneration);
        TryShutdownStep(_ttsQueue.EmergencyStop);
        TryShutdownStep(_ipcServer.Dispose);
        return DisposeCoreAsync();
    }

    private async Task DisposeCoreAsync()
    {
        TryShutdownStep(SaveSettings);

        // Calling connector disposal initiates cancellation immediately. Never
        // wait for auto-connect before that cancellation has been requested.
        Task connectorShutdown = StartShutdownTask(() => _sourceConnector.DisposeAsync());

        await IgnoreShutdownFailureAsync(connectorShutdown);
        await IgnoreShutdownFailureAsync(_autoConnectTask);
        await IgnoreShutdownFailureAsync(_incomingEventPumpTask);
        await IgnoreShutdownFailureAsync(StartShutdownTask(() => _voicePreviewOutput.DisposeAsync()));
        await IgnoreShutdownFailureAsync(StartShutdownTask(() => _ttsQueue.DisposeAsync()));
        await IgnoreShutdownFailureAsync(StartShutdownTask(() => _auditLogger.DisposeAsync()));

        TryShutdownStep(_pipeline.Dispose);
        TryShutdownStep(_ttsEngine.Dispose);
        TryShutdownStep(_audioRouter.Dispose);
        TryShutdownStep(_voicePreviewAudioRouter.Dispose);
        TryShutdownStep(_announcer.Dispose);
        TryShutdownStep(_incomingEventCts.Dispose);
    }

    private static Task StartShutdownTask(Func<ValueTask> operation)
    {
        try { return operation().AsTask(); }
        catch { return Task.CompletedTask; }
    }

    private static async Task IgnoreShutdownFailureAsync(Task task)
    {
        try { await task; }
        catch { }
    }

    private static void TryShutdownStep(Action operation)
    {
        try { operation(); }
        catch { }
    }

    private void SaveSettings()
    {
        _settings.CaptureModerationConfig(Config);
        _settings.SelectedAudioEndpointId = string.IsNullOrEmpty(SelectedAudioEndpoint) ? null : SelectedAudioEndpoint;
        _settings.SelectedBroadcastEndpointId = string.IsNullOrEmpty(SelectedAudioEndpoint) ? null : SelectedAudioEndpoint;
        _settings.SelectedVoiceName = string.IsNullOrEmpty(SelectedVoice) ? null : SelectedVoice;
        _settings.SpeechRate = Math.Clamp(SpeechRate, -5, 5);
        _settings.SpeechVolume = Math.Clamp(SpeechVolume, 0, 100);
        _settings.ReaderSpeechRate = Math.Clamp(ReaderSpeechRate, -5, 5);
        SaveSettingsOrReport();
    }

    private void PersistModerationSettings()
    {
        _settings.CaptureModerationConfig(Config);
        SaveSettingsOrReport();
        OnPropertyChanged(nameof(Config));
    }

    private void SaveSettingsOrReport()
    {
        if (_settings.TrySave(out string? error)) return;

        string message = string.IsNullOrWhiteSpace(error)
            ? "SafeSpeak could not save your settings."
            : $"SafeSpeak could not save your settings. {error}";
        LiveStatusAnnouncement = message;
        _announcer?.Announce(message, interrupt: true);
    }
}
