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
    private readonly SourceConnectorHost _sourceConnector;
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
    private readonly List<ModerationDecision> _heldLiveFeedDecisions = new();
    private int _moderationGuideSectionIndex;
    private Task _incomingEventPumpTask = Task.CompletedTask;
    private int _droppedIncomingEventCount;
    private int _queueSaturationAnnounced;
    private int _monitoringGeneration;
    private Task _autoConnectTask = Task.CompletedTask;
    private readonly object _disposeLock = new();
    private Task? _disposeTask;
    private bool _isInitializing = true;
    private string? _pendingGuidanceDeviceNotice;

    [ObservableProperty]
    private string _connectionStatusText = "Disconnected";

    [ObservableProperty]
    private string _selectedSourceConnectorId = TikFinityWebSocketClient.ConnectorDescriptor.Id;

    [ObservableProperty]
    private string _tikTokUsername = "";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _selectedAudioEndpoint = "";

    [ObservableProperty]
    private string _selectedGuidanceAudioEndpoint = "";

    [ObservableProperty]
    private string _selectedVoice = "";

    [ObservableProperty]
    private int _speechRate = 0;

    [ObservableProperty]
    private int _speechVolume = 100;

    [ObservableProperty]
    private int _readerSpeechRate = 3;

    [ObservableProperty]
    private int _readerSpeechVolume = 100;

    [ObservableProperty]
    private bool _narrateDetailedHelp = true;

    [ObservableProperty]
    private bool _narrateTypedCharacters;

    [ObservableProperty]
    private int _interfaceTextScalePercent = 100;

    [ObservableProperty]
    private int _queueLimit = 50;

    [ObservableProperty]
    private int _interMessageGapTenths;

    [ObservableProperty]
    private bool _adaptiveInterMessageGap = true;

    [ObservableProperty]
    private bool _messageRateLimitEnabled = true;

    [ObservableProperty]
    private MessageRateWindow _selectedMessageRateWindow = MessageRateWindow.TenSeconds;

    [ObservableProperty]
    private int _perUserMessageLimit = 3;

    [ObservableProperty]
    private int _streamMessageLimit = 500;

    [ObservableProperty]
    private string _customBlockedInput = "";

    [ObservableProperty]
    private string _customAllowedInput = "";

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
    private ModerationModelPreference _selectedModerationModel =
        ModerationModelPreference.BuiltInHybrid;

    [ObservableProperty]
    private string? _selectedCustomBlockedTerm;

    [ObservableProperty]
    private string? _selectedCustomAllowedTerm;

    [ObservableProperty]
    private string _filterTestInput = string.Empty;

    [ObservableProperty]
    private string _filterTestResult = "No filter test has been run.";

    [ObservableProperty]
    private bool _filterTestPassed;

    [ObservableProperty]
    private bool _hasFilterTestResult;

    [ObservableProperty]
    private bool _areSafetyGuideControlsAtTop = true;

    [ObservableProperty]
    private bool _isLiveFeedReviewPaused;

    [ObservableProperty]
    private int _heldLiveFeedCount;

    public ObservableCollection<LiveFeedEntryViewModel> LiveFeed { get; } = new();
    public ObservableCollection<AudioEndpointInfo> AudioEndpoints { get; } = new();
    public ObservableCollection<VoiceInfo> Voices { get; } = new();
    public ObservableCollection<string> CustomBlockedTerms { get; } = new();
    public ObservableCollection<string> CustomAllowedTerms { get; } = new();
    public IReadOnlyList<ThemeChoice> ThemeChoices { get; } =
    [
        new(ThemePreference.Light, "Light", 1),
        new(ThemePreference.Dark, "Dark", 2),
        new(ThemePreference.HighContrast, "High Contrast", 3)
    ];
    public IReadOnlyList<ModerationModelChoice> ModerationModelChoices { get; } =
    [
        new(
            ModerationModelPreference.BuiltInHybrid,
            "Built-in enhanced model (recommended)",
            1),
        new(
            ModerationModelPreference.Qwen3Guard06BCompressed,
            "Qwen3Guard 0.6B compressed (optional)",
            2)
    ];
    public IReadOnlyList<AudienceChoice> AudienceChoices { get; } =
    [
        new(AudienceMode.All, "Everyone", 1),
        new(AudienceMode.FollowersOnly, "Followers", 2),
        new(AudienceMode.SubscribersOnly, "Subscribers", 3),
        new(AudienceMode.ModeratorsOnly, "Moderators", 4)
    ];
    public IReadOnlyList<MessageRateWindowChoice> MessageRateWindowChoices { get; } =
    [
        new(MessageRateWindow.OneSecond, "Per 1 second", 1),
        new(MessageRateWindow.TenSeconds, "Per 10 seconds", 2)
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
    public string AllowedRulesSummary => CustomAllowedTerms.Count == 1
        ? "1 custom allowed term"
        : $"{CustomAllowedTerms.Count} custom allowed terms";
    public string QueueLimitAccessibleText =>
        $"Approved message queue limit: {Math.Clamp(QueueLimit, 1, 500)} pending items.";
    public string InterMessageGapAccessibleText =>
        $"Maximum time between queued voices: {Math.Clamp(InterMessageGapTenths, 0, 50) / 10.0:0.0} seconds. " +
        (AdaptiveInterMessageGap
            ? "Adaptive spacing is on and shortens this delay as the queue fills."
            : "Adaptive spacing is off and this delay stays fixed.");
    public string InterMessageGapDisplay =>
        $"{Math.Clamp(InterMessageGapTenths, 0, 50) / 10.0:0.0} s";
    public string MessageRateWindowAccessibleText =>
        $"Spam limit time window: {(SelectedMessageRateWindow == MessageRateWindow.OneSecond ? "1 second" : "10 seconds")}.";
    public string PerUserMessageLimitAccessibleText =>
        $"Per-viewer spam limit: {Math.Clamp(PerUserMessageLimit, 1, 100)} messages {MessageRateWindowPhrase}.";
    public string StreamMessageLimitAccessibleText =>
        $"Whole-stream spam limit: {Math.Clamp(StreamMessageLimit, 10, 5000)} messages {MessageRateWindowPhrase}.";
    private string MessageRateWindowPhrase =>
        SelectedMessageRateWindow == MessageRateWindow.OneSecond
            ? "per second"
            : "per 10 seconds";
    public int RetainedLiveFeedCount => _heldLiveFeedDecisions.Count;
    public int DroppedHeldLiveFeedCount => Math.Max(0, HeldLiveFeedCount - RetainedLiveFeedCount);
    public string LiveFeedReviewStatus => IsLiveFeedReviewPaused
        ? HeldLiveFeedCount == 0
            ? "Live activity is paused for review. No new messages are being held."
            : DroppedHeldLiveFeedCount == 0
                ? $"Live activity is paused for review. {HeldLiveFeedCount} new " +
                  $"{(HeldLiveFeedCount == 1 ? "message is" : "messages are")} being held."
                : $"Live activity is paused for review. {HeldLiveFeedCount} new messages were received; " +
                  $"the newest {RetainedLiveFeedCount} are held and {DroppedHeldLiveFeedCount} older " +
                  $"{(DroppedHeldLiveFeedCount == 1 ? "entry was" : "entries were")} not retained."
        : "Live activity is updating. Move keyboard focus into the list to pause visual updates while reviewing it.";
    public string EvasionRulesSummary => "Unicode and spacing protection active";
    public ScreenReaderAnnouncer Announcer => _announcer;
    public string SpokenGuidanceStatus => !SpokenGuidanceEnabled
        ? "SafeSpeak spoken guidance is disabled; Windows Narrator and other UI Automation readers remain supported"
        : _announcer.IsSpeechAvailable
            ? $"SafeSpeak spoken guidance is enabled on {GuidanceAudioEndpointName}"
            : "SafeSpeak spoken guidance is enabled, but Windows speech is unavailable; Windows Narrator and other UI Automation readers remain supported";
    public string GuidanceAudioEndpointName =>
        AudioEndpointFormatter.GetFriendlyName(AudioEndpoints, SelectedGuidanceAudioEndpoint);
    public string GuidanceAudioEndpointAccessibleText =>
        $"Built-in guidance audio device: {GuidanceAudioEndpointName}.";
    public string InterfaceTextScaleAccessibleText =>
        $"Interface text size: {Math.Clamp(InterfaceTextScalePercent, 100, 200)} percent.";
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
    public IReadOnlyList<SourceConnectorChoice> SourceConnectorChoices { get; } =
    [
        new(TikFinityWebSocketClient.ConnectorDescriptor.Id, "TikFinity (local app)"),
        new(TikTokLiveConnector.ConnectorDescriptor.Id, "TikTok Direct by username")
    ];
    public string SourceName => _sourceConnector.Descriptor.DisplayName;
    public string SourceDescription =>
        $"{_sourceConnector.Descriptor.ProviderName}. {_sourceConnector.EndpointDescription}";
    public string IntentModelStatus => _pipeline.Classifier switch
    {
        Qwen3GuardIntentClassifier qwen when qwen.IsModelLoaded =>
            "Qwen3Guard 0.6B compressed is installed, verified, and active through SafeSpeak's private local service. The built-in model remains active as its safety fallback.",
        Qwen3GuardIntentClassifier when IsInstallingModerationModel =>
            $"Qwen3Guard installation is in progress: {Math.Round(ModerationModelDownloadProgress):0} percent. The built-in local model remains active.",
        Qwen3GuardIntentClassifier when !_qwenRuntime.IsRuntimeAvailable =>
            "Qwen3Guard is selected, but this development build is missing SafeSpeak's packaged local model runtime. The built-in local model remains active.",
        Qwen3GuardIntentClassifier when !IsQwenModelInstalled =>
            "Qwen3Guard 0.6B compressed is selected but not installed. Use the Install optional model button; no separate application or terminal command is needed. Until installation finishes, the built-in local model remains active.",
        Qwen3GuardIntentClassifier =>
            "Qwen3Guard 0.6B compressed is installed and its private local service is starting. The built-in local model remains active until Qwen3Guard responds.",
        LocalOnnxIntentClassifier local when local.IsModelLoaded =>
            "Enhanced filtering model: bundled, installed, and active on this computer.",
        LocalOnnxIntentClassifier local =>
            $"Enhanced filtering model: unavailable. Deterministic local filtering remains active. {local.AvailabilityMessage}",
        _ =>
            $"Selected intent moderation engine: {_pipeline.Classifier.ModelName}. The bundled local model remains packaged as its fallback."
    };
    public string IntentModelShortStatus => _pipeline.Classifier switch
    {
        Qwen3GuardIntentClassifier qwen when qwen.IsModelLoaded =>
            "Qwen3Guard 0.6B — Active",
        Qwen3GuardIntentClassifier when IsInstallingModerationModel =>
            $"Qwen3Guard — Installing {Math.Round(ModerationModelDownloadProgress):0}%",
        Qwen3GuardIntentClassifier when !IsQwenModelInstalled =>
            "Qwen3Guard — Install available",
        Qwen3GuardIntentClassifier =>
            "Qwen3Guard selected — Built-in fallback active",
        LocalOnnxIntentClassifier local when local.IsModelLoaded =>
            "Enhanced model — Active",
        LocalOnnxIntentClassifier =>
            "Local fallback — Active",
        _ => $"{_pipeline.Classifier.ModelName} — Active"
    };
    public string ModerationModelSelectionAccessibleText
    {
        get
        {
            int index = ModerationModelChoices
                .Select((choice, position) => (choice, position))
                .First(item => item.choice.Value == SelectedModerationModel)
                .position;
            return $"{ModerationModelChoices[index].DisplayName}, option {index + 1} of {ModerationModelChoices.Count}. {IntentModelStatus}";
        }
    }
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
        1 => "Contextual wording is blocked only when its hostility score is 90% or higher.",
        2 => "Contextual wording is blocked when its hostility score is 75% or higher.",
        3 => "Contextual wording is blocked when its hostility score is 60% or higher. Recommended for most streams.",
        4 => "Contextual wording is blocked when its hostility score is 45% or higher, increasing false-positive risk.",
        _ => "Contextual wording is blocked when its hostility score is 60% or higher. Recommended for most streams."
    };
    public string ModerationLevelAccessibleText =>
        $"Moderation strength selector: {ModerationLevelName}, level {Math.Clamp(ModerationLevel, 1, 4)} of 4. {ModerationLevelDescription}";
    public string ModerationStrengthSummary =>
        $"{ModerationLevelName} ({Math.Clamp(ModerationLevel, 1, 4)} of 4)";
    public string ModerationLevelThresholdSummary =>
        $"Current contextual cutoff: {Config.IntentToxicityThreshold:P0}. Scores at or above this value are blocked.";
    public IReadOnlyList<string> ModerationLevelGuide { get; } =
    [
        "Level 1, Relaxed — 90% cutoff. Permits most rude or unhinged language; stops only the most severe model findings and always-on safety rules.",
        "Level 2, Balanced — 75% cutoff. Also blocks clear directed hate, severe harassment, hostile profanity, and malicious harm wishes.",
        "Level 3, Strong — 60% cutoff. Also blocks directed insults, aggressive degradation, and concerning ambiguous safety language.",
        "Level 4, Maximum — 45% cutoff. Also blocks mild insults, hostile dismissal, and most borderline aggressive language."
    ];
    public IReadOnlyList<string> ContextualIntentSignals { get; } =
    [
        "Who or what the negative wording targets: a person, group, streamer, stream, or a non-human game object.",
        "What the speaker is doing: endorsing or requesting harm versus condemning, preventing, or reporting it.",
        "How severe and certain the signal is: threats, intimidation, malicious wishes, degradation, profanity, or ambiguous euphemisms.",
        "Identity-based hostility, severe toxicity, and obscene abuse detected after anti-evasion normalization."
    ];
    public IReadOnlyList<string> AcceptedHostilityExamples { get; } =
    [
        "“I hate this game.” — frustration is aimed at a game.",
        "“This boss is stupid.” — a rude word describes an in-game object.",
        "“This update is awful.” — criticism is aimed at software.",
        "“I hate losing this round.” — frustration describes an activity and outcome."
    ];
    public IReadOnlyList<string> RejectedHostilityExamples { get; } =
    [
        "Level 2 and above: “I hate you.” — hate is aimed directly at a person.",
        "Level 2 and above: “I hate this stream.” — hostility targets the broadcaster's space.",
        "Level 2 and above: “Everyone in this chat is trash.” — hostility targets a group.",
        "Level 3 and above: “I'm excited to do things with my niece.” — a vague family euphemism receives a 70% concern score.",
        "Every level: explicit sexual intent involving a child or underage person."
    ];
    public IReadOnlyList<string> SliderDependentHostilityExamples { get; } =
    [
        "Relaxed is deliberately permissive. Directed hate is anchored below its 90% cutoff, but credible threats and the hard safety floor can still be blocked.",
        "Balanced adds clear directed hate and severe harassment; Strong adds directed insults and ambiguous high-risk wording; Maximum adds mild hostility.",
        "Non-human game frustration and complete protective statements remain below even the Maximum cutoff."
    ];
    public IReadOnlyList<string> SensitiveContextExamples { get; } =
    [
        "Allowed: “I'm excited to play video games with my niece.” A family term plus a specific harmless activity is not evidence of abuse.",
        "Allowed: “Sexual abuse of children is wrong.” The speech act clearly condemns harm.",
        "Levels 3-4 block: “I'm excited to do things with my niece.” One message cannot establish age or intent, so the app identifies concern without labeling the speaker.",
        "Every level blocks: explicit sexual intent, sexualization, or solicitation involving a child or underage person."
    ];
    public IReadOnlyList<string> AlwaysOnModerationLayers { get; } =
    [
        "Built-in severe-abuse phrases and your custom banned words or phrases.",
        "Explicit sexual exploitation, sexualization, or solicitation involving a child or underage person.",
        "Unicode, spacing, and character-substitution normalization before blocked-term checks.",
        "Message length and audience eligibility before contextual classification.",
        "Mixed-writing-system protection and English-only filtering when their switches are enabled.",
        "Fail-closed protection: a message is blocked if contextual classification is unavailable."
    ];
    public string ModerationGuideNarration => string.Join(
        " ",
        new[]
        {
            "Moderation guide.",
            ModerationLevelAccessibleText,
            "The slider changes the minimum contextual-hostility score required to block a message. Always-on safety layers remain active.",
            "Contextual filtering model.",
            IntentModelStatus,
            "All four cutoffs."
        }
        .Concat(ModerationLevelGuide)
        .Concat(["Contextual wording signals."])
        .Concat(ContextualIntentSignals)
        .Concat(["Examples accepted at every level."])
        .Concat(AcceptedHostilityExamples)
        .Concat(["Examples blocked as strictness increases."])
        .Concat(RejectedHostilityExamples)
        .Concat(["How the slider changes filtering."])
        .Concat(SliderDependentHostilityExamples)
        .Concat(["Sensitive child-safety context."])
        .Concat(SensitiveContextExamples)
        .Concat(["Always checked at every slider level."])
        .Concat(AlwaysOnModerationLayers)
        .Concat([
            "Intent means a local model scores one message for hostile or harmful meaning. " +
            "It does not see earlier messages or know relationships, tone of voice, sarcasm, or shared jokes. " +
            "The result answers whether a message is safe to speak; it is not a judgment of the sender's motive."
        ]));
    public bool AreSafetyGuideControlsAtEnd => !AreSafetyGuideControlsAtTop;
    public string SafetyGuideVisibilityButtonText =>
        AreSafetyGuideControlsAtTop ? "Move controls to bottom" : "Move controls to top";
    public string SafetyGuideVisibilityButtonAutomationName =>
        AreSafetyGuideControlsAtTop
            ? "Move the Safety guide controls to the bottom of the page"
            : "Move the Safety guide controls to the top of the page";

    [RelayCommand]
    public void ReadModerationGuide()
    {
        AnnounceNarration(
            ModerationGuideNarration,
            "Reading the full moderation guide. Use Shut up built-in guidance to stop it.",
            interrupt: true);
    }

    [RelayCommand]
    public void ReadNextModerationGuideSection()
    {
        IReadOnlyList<(string Title, string Text)> sections = GetModerationGuideSections();
        if (_moderationGuideSectionIndex >= sections.Count)
        {
            _moderationGuideSectionIndex = 0;
        }

        (string title, string text) = sections[_moderationGuideSectionIndex];
        int spokenPosition = _moderationGuideSectionIndex + 1;
        _moderationGuideSectionIndex++;
        AnnounceNarration(
            $"{title}. {text}",
            $"Playing Safety guide page {spokenPosition} of {sections.Count}: {title}.",
            interrupt: true);
    }

    [RelayCommand]
    public void RestartModerationGuide()
    {
        _moderationGuideSectionIndex = 0;
        AnnounceState("Safety guide reset to page 1. Choose Play guide page when ready.");
    }

    [RelayCommand]
    public void ToggleSafetyGuideVisibility()
    {
        AreSafetyGuideControlsAtTop = !AreSafetyGuideControlsAtTop;
        AnnounceState(AreSafetyGuideControlsAtTop
            ? "Safety guide controls moved to the top of the page. The button you pressed moved with them and is now labeled Move controls to bottom. Navigate to the top of the Safety page to find it again."
            : "Safety guide controls moved to the bottom of the page. The button you pressed moved with them and is now labeled Move controls to top. Navigate to the bottom of the Safety page to find it again. The guide text remains visible.");
    }

    partial void OnAreSafetyGuideControlsAtTopChanged(bool value)
    {
        OnPropertyChanged(nameof(AreSafetyGuideControlsAtEnd));
        OnPropertyChanged(nameof(SafetyGuideVisibilityButtonText));
        OnPropertyChanged(nameof(SafetyGuideVisibilityButtonAutomationName));
    }

    private IReadOnlyList<(string Title, string Text)> GetModerationGuideSections() =>
    [
        ("Current moderation strength", $"{ModerationLevelAccessibleText} The slider changes the minimum contextual-hostility score required to block a message. Always-on safety layers remain active."),
        ("Contextual filtering model", IntentModelStatus + " Focus the model selector, then press Enter, Space, Alt plus Down Arrow, or F4 to open its choices. Arrow keys change the model only while the list is open. When Qwen3Guard is selected, use Install optional model, Cancel model download, or Remove optional model directly below the selector. SafeSpeak manages the runtime; no separate application or terminal command is needed."),
        ("All four cutoffs", string.Join(" ", ModerationLevelGuide)),
        ("Contextual wording signals", string.Join(" ", ContextualIntentSignals)),
        ("Examples accepted at every level", string.Join(" ", AcceptedHostilityExamples)),
        ("Examples blocked as strictness increases", string.Join(" ", RejectedHostilityExamples)),
        ("How the slider changes filtering", string.Join(" ", SliderDependentHostilityExamples)),
        ("Sensitive child-safety context", string.Join(" ", SensitiveContextExamples)),
        ("Always checked at every slider level", string.Join(" ", AlwaysOnModerationLayers)),
        ("Limits of intent scoring", "Intent means a local model scores one message for hostile or harmful meaning. It does not see earlier messages or know relationships, tone of voice, sarcasm, or shared jokes. The result answers whether a message is safe to speak; it is not a judgment of the sender's motive.")
    ];

    public void PauseLiveFeedReview()
    {
        if (IsLiveFeedReviewPaused)
        {
            return;
        }

        IsLiveFeedReviewPaused = true;
        AnnounceState("Live activity paused for review. New visual entries will be held while moderation and speech continue.");
    }

    public void ResumeLiveFeedReview()
    {
        if (!IsLiveFeedReviewPaused)
        {
            return;
        }

        int receivedCount = HeldLiveFeedCount;
        int retainedCount = _heldLiveFeedDecisions.Count;
        int droppedCount = Math.Max(0, receivedCount - retainedCount);
        foreach (ModerationDecision decision in _heldLiveFeedDecisions)
        {
            AddDecisionToLiveFeed(decision);
        }

        _heldLiveFeedDecisions.Clear();
        HeldLiveFeedCount = 0;
        IsLiveFeedReviewPaused = false;
        AnnounceState(receivedCount == 0
            ? "Live activity review ended. The feed is updating again."
            : droppedCount == 0
                ? $"Live activity review ended. {retainedCount} held " +
                  $"{(retainedCount == 1 ? "message is" : "messages are")} now available, and the feed is updating again."
                : $"Live activity review ended. The newest {retainedCount} of {receivedCount} received messages are now available. " +
                  $"{droppedCount} older {(droppedCount == 1 ? "entry was" : "entries were")} not retained.");
    }

    [RelayCommand]
    public void AnnounceLiveFeedReviewStatus() =>
        AnnounceNarration(
            LiveFeedReviewStatus,
            "Live activity review status spoken.",
            interrupt: true);

    public sealed record ThemeChoice(
        ThemePreference Value,
        string DisplayName,
        int Position);

    public sealed record ModerationModelChoice(
        ModerationModelPreference Value,
        string DisplayName,
        int Position);

    public sealed record AudienceChoice(
        AudienceMode Value,
        string DisplayName,
        int Position);

    public sealed record MessageRateWindowChoice(
        MessageRateWindow Value,
        string DisplayName,
        int Position);

    public sealed record SourceConnectorChoice(
        string Id,
        string DisplayName);

    private readonly KokoroModelManager _kokoroManager;

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _pipeline = new ModerationPipeline(
            _settings.CreateModerationConfig(),
            intentClassifier: CreateSelectedIntentClassifier());
        _moderationTestService = new ModerationTestService(_pipeline);
        SelectedModerationModel = _settings.ModerationModel;
        ModerationModelDownloadStatus = IsQwenModelInstalled
            ? "The optional Qwen3Guard model is installed."
            : "The optional Qwen3Guard model is not installed.";
        ModerationModelAccessibleProgress = ModerationModelDownloadStatus;
        var connectorRegistry = SourceConnectorRegistry.CreateDefault(_settings.TikTokUsername);
        string connectorId = connectorRegistry.Descriptors.Any(
            descriptor => string.Equals(
                descriptor.Id,
                _settings.SelectedSourceConnectorId,
                StringComparison.OrdinalIgnoreCase))
            ? _settings.SelectedSourceConnectorId
            : TikFinityWebSocketClient.ConnectorDescriptor.Id;
        _sourceConnector = new SourceConnectorHost(connectorRegistry.Create(connectorId));
        _settings.SelectedSourceConnectorId = _sourceConnector.Descriptor.Id;
        SelectedSourceConnectorId = _settings.SelectedSourceConnectorId;
        TikTokUsername = _settings.TikTokUsername;
        _kokoroManager = new KokoroModelManager();
        _ttsEngine = new ModularTtsEngine(_kokoroManager);
        _audioRouter = new WasapiAudioRouter();
        _voicePreviewAudioRouter = new WasapiAudioRouter();
        _ttsQueue = new TtsQueue(
            _ttsEngine,
            _audioRouter,
            Math.Clamp(_settings.QueueLimit, 1, 500));
        _voicePreviewOutput = new PrivateVoicePreviewOutput(_ttsEngine, _voicePreviewAudioRouter);
        _auditLogger = new StreamAuditLogger();
        _auditLogger.IsEnabled = _settings.EnableStreamAuditLogging;
        EnableStreamAuditLogging = _settings.EnableStreamAuditLogging;
        SelectedAudioEndpoint = _settings.SelectedBroadcastEndpointId ?? _settings.SelectedAudioEndpointId ?? string.Empty;
        SelectedGuidanceAudioEndpoint = _settings.SelectedGuidanceAudioEndpointId ?? string.Empty;
        SelectedVoice = _settings.SelectedVoiceName ?? string.Empty;
        SpeechRate = Math.Clamp(_settings.SpeechRate, -5, 5);
        SpeechVolume = Math.Clamp(_settings.SpeechVolume, 0, 150);
        ReaderSpeechRate = Math.Clamp(_settings.ReaderSpeechRate, -5, 5);
        ReaderSpeechVolume = Math.Clamp(_settings.ReaderSpeechVolume, 0, 150);
        NarrateDetailedHelp = _settings.NarrateDetailedHelp;
        NarrateTypedCharacters = _settings.NarrateTypedCharacters;
        InterfaceTextScalePercent = Math.Clamp(_settings.InterfaceTextScalePercent, 100, 200);
        QueueLimit = Math.Clamp(_settings.QueueLimit, 1, 500);
        InterMessageGapTenths = Math.Clamp(
            (int)Math.Round(_settings.InterMessageGapMilliseconds / 100.0),
            0,
            50);
        AdaptiveInterMessageGap = _settings.AdaptiveInterMessageGap;
        MessageRateLimitEnabled = Config.MessageRateLimitEnabled;
        SelectedMessageRateWindow = Enum.IsDefined(Config.MessageRateWindow)
            ? Config.MessageRateWindow
            : MessageRateWindow.TenSeconds;
        PerUserMessageLimit = Math.Clamp(Config.PerUserMessageLimit, 1, 100);
        StreamMessageLimit = Math.Clamp(Config.StreamMessageLimit, 10, 5000);
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
        foreach (string term in Config.CustomAllowedTerms
                     .Where(term => !string.IsNullOrWhiteSpace(term))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(term => term, StringComparer.CurrentCultureIgnoreCase))
        {
            CustomAllowedTerms.Add(term);
        }
        _announcer = new ScreenReaderAnnouncer();
        RefreshAccessibilitySettingsFromStore();
        _announcer.SpeechRate = ReaderSpeechRate;
        _announcer.SpeechVolume = ReaderSpeechVolume;
        InitializeGlobalShortcuts();

        _ipcServer = new StreamDeckIpcServer(
            stateProvider: GetIpcState,
            commandHandler: HandleIpcCommandAsync
        );

        _incomingEventPumpTask = ProcessIncomingEventsAsync(_incomingEventCts.Token);
        WireEvents();
        LoadSystemAudioAndVoices();

        _ipcServer.Start();
        _isInitializing = false;
        if (!string.IsNullOrWhiteSpace(_pendingGuidanceDeviceNotice))
        {
            _announcer.Announce(_pendingGuidanceDeviceNotice);
        }
        _autoConnectTask = AutoConnectSourceAsync();
        _ = StartInstalledQwenModelAsync();
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
            ConnectionStatusText = string.IsNullOrWhiteSpace(e.Message)
                ? $"{SourceName}: {e.State}"
                : $"{SourceName}: {e.State}. {e.Message}";
            OnPropertyChanged(nameof(SourceName));
            OnPropertyChanged(nameof(SourceDescription));
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
                else if (e.State == ConnectionState.Faulted)
                {
                    LiveStatusAnnouncement = string.IsNullOrWhiteSpace(e.Message)
                        ? $"{SourceName} could not connect."
                        : e.Message;
                    AnnounceState(LiveStatusAnnouncement);
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
        string requestedGuidanceEndpoint = SelectedGuidanceAudioEndpoint;
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
        if (string.IsNullOrEmpty(SelectedGuidanceAudioEndpoint) ||
            !AudioEndpoints.Any(e => e.Id == SelectedGuidanceAudioEndpoint))
        {
            SelectedGuidanceAudioEndpoint = AudioEndpoints.FirstOrDefault(e => e.IsDefault)?.Id
                ?? AudioEndpoints.FirstOrDefault()?.Id
                ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(requestedGuidanceEndpoint))
            {
                _pendingGuidanceDeviceNotice =
                    $"The saved guidance audio device is unavailable. Using {GuidanceAudioEndpointName}.";
                LiveStatusAnnouncement = _pendingGuidanceDeviceNotice;
            }
        }
        _announcer.SelectAudioEndpoint(
            string.IsNullOrEmpty(SelectedGuidanceAudioEndpoint)
                ? null
                : SelectedGuidanceAudioEndpoint);
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

    partial void OnSelectedGuidanceAudioEndpointChanged(string value)
    {
        _announcer?.SelectAudioEndpoint(string.IsNullOrEmpty(value) ? null : value);
        OnPropertyChanged(nameof(GuidanceAudioEndpointName));
        OnPropertyChanged(nameof(GuidanceAudioEndpointAccessibleText));
        OnPropertyChanged(nameof(SpokenGuidanceStatus));
        if (_isInitializing) return;
        _settings.SelectedGuidanceAudioEndpointId = string.IsNullOrEmpty(value) ? null : value;
        SaveSettingsOrReport();
        AudioEndpointInfo? endpoint = AudioEndpoints.FirstOrDefault(
            candidate => string.Equals(candidate.Id, value, StringComparison.Ordinal));
        AnnounceOptionSelection(
            "Built-in guidance audio device",
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
        _announcer.AnnounceFocus($"{category}: {optionName}{position}.");
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
        if (_ttsQueue is not null) _ttsQueue.SpeechVolume = Math.Clamp(value, 0, 150);
        UpdateVoicePreviewSettings();
        if (_isInitializing) return;
        _settings.SpeechVolume = Math.Clamp(value, 0, 150);
        SaveSettingsOrReport();
    }

    partial void OnReaderSpeechRateChanged(int value)
    {
        if (_announcer is not null) _announcer.SpeechRate = Math.Clamp(value, -5, 5);
        if (_isInitializing) return;
        _settings.ReaderSpeechRate = Math.Clamp(value, -5, 5);
        SaveSettingsOrReport();
    }

    partial void OnReaderSpeechVolumeChanged(int value)
    {
        int normalized = Math.Clamp(value, 0, 150);
        if (value != normalized)
        {
            ReaderSpeechVolume = normalized;
            return;
        }

        if (_announcer is not null) _announcer.SpeechVolume = normalized;
        if (_isInitializing) return;
        _settings.ReaderSpeechVolume = normalized;
        SaveSettingsOrReport();
    }

    partial void OnNarrateDetailedHelpChanged(bool value)
    {
        if (_isInitializing) return;
        _settings.NarrateDetailedHelp = value;
        SaveSettingsOrReport();
        AnnounceState(value
            ? "Detailed control instructions will be read when focus moves."
            : "Detailed control instructions on focus are off. Focused controls will use short names, values, and states.");
    }

    partial void OnNarrateTypedCharactersChanged(bool value)
    {
        if (_isInitializing) return;
        _settings.NarrateTypedCharacters = value;
        SaveSettingsOrReport();
        AnnounceState(value ? "Typing echo enabled." : "Typing echo disabled.");
    }

    partial void OnInterfaceTextScalePercentChanged(int value)
    {
        int normalized = Math.Clamp(value, 100, 200);
        if (value != normalized)
        {
            InterfaceTextScalePercent = normalized;
            return;
        }

        ThemeManager.ApplyTextScale(normalized);
        OnPropertyChanged(nameof(InterfaceTextScaleAccessibleText));
        if (_isInitializing) return;
        _settings.InterfaceTextScalePercent = normalized;
        SaveSettingsOrReport();
    }

    partial void OnQueueLimitChanged(int value)
    {
        int normalized = Math.Clamp(value, 1, 500);
        if (value != normalized)
        {
            QueueLimit = normalized;
            return;
        }

        _ttsQueue?.SetCapacity(normalized);
        OnPropertyChanged(nameof(QueueLimitAccessibleText));
        if (_isInitializing) return;
        _settings.QueueLimit = normalized;
        SaveSettingsOrReport();
        AnnounceState($"Approved message queue limit changed to {normalized}.");
    }

    partial void OnInterMessageGapTenthsChanged(int value)
    {
        int normalized = Math.Clamp(value, 0, 50);
        if (value != normalized)
        {
            InterMessageGapTenths = normalized;
            return;
        }

        if (_ttsQueue is not null) _ttsQueue.InterMessageGapMilliseconds = normalized * 100;
        OnPropertyChanged(nameof(InterMessageGapAccessibleText));
        OnPropertyChanged(nameof(InterMessageGapDisplay));
        if (_isInitializing) return;
        _settings.InterMessageGapMilliseconds = normalized * 100;
        SaveSettingsOrReport();
    }

    partial void OnAdaptiveInterMessageGapChanged(bool value)
    {
        if (_ttsQueue is not null) _ttsQueue.AdaptiveInterMessageGap = value;
        OnPropertyChanged(nameof(InterMessageGapAccessibleText));
        if (_isInitializing) return;
        _settings.AdaptiveInterMessageGap = value;
        SaveSettingsOrReport();
        AnnounceState(value
            ? "Adaptive queued-voice spacing enabled. The delay shortens as the queue fills."
            : "Adaptive queued-voice spacing disabled. The selected delay stays fixed.");
    }

    partial void OnMessageRateLimitEnabledChanged(bool value)
    {
        Config.MessageRateLimitEnabled = value;
        _pipeline?.Rules.ResetCooldowns();
        if (_isInitializing) return;
        PersistModerationSettings();
        AnnounceState(value
            ? "Message spam limits enabled."
            : "Message spam limits disabled. Saved limit values remain available.");
    }

    partial void OnSelectedMessageRateWindowChanged(MessageRateWindow value)
    {
        MessageRateWindow normalized = Enum.IsDefined(value)
            ? value
            : MessageRateWindow.TenSeconds;
        if (value != normalized)
        {
            SelectedMessageRateWindow = normalized;
            return;
        }

        Config.MessageRateWindow = normalized;
        _pipeline?.Rules.ResetCooldowns();
        OnPropertyChanged(nameof(MessageRateWindowAccessibleText));
        OnPropertyChanged(nameof(PerUserMessageLimitAccessibleText));
        OnPropertyChanged(nameof(StreamMessageLimitAccessibleText));
        if (_isInitializing) return;
        PersistModerationSettings();
        AnnounceOptionSelection(
            "Spam limit time window",
            MessageRateWindowChoices.First(choice => choice.Value == normalized).DisplayName,
            normalized == MessageRateWindow.OneSecond ? 0 : 1,
            MessageRateWindowChoices.Count);
    }

    partial void OnPerUserMessageLimitChanged(int value)
    {
        int normalized = Math.Clamp(value, 1, 100);
        if (value != normalized)
        {
            PerUserMessageLimit = normalized;
            return;
        }

        Config.PerUserMessageLimit = normalized;
        _pipeline?.Rules.ResetCooldowns();
        OnPropertyChanged(nameof(PerUserMessageLimitAccessibleText));
        if (_isInitializing) return;
        PersistModerationSettings();
    }

    partial void OnStreamMessageLimitChanged(int value)
    {
        int normalized = Math.Clamp(value, 10, 5000);
        if (value != normalized)
        {
            StreamMessageLimit = normalized;
            return;
        }

        Config.StreamMessageLimit = normalized;
        _pipeline?.Rules.ResetCooldowns();
        OnPropertyChanged(nameof(StreamMessageLimitAccessibleText));
        if (_isInitializing) return;
        PersistModerationSettings();
    }

    partial void OnIsLiveFeedReviewPausedChanged(bool value) =>
        OnPropertyChanged(nameof(LiveFeedReviewStatus));

    partial void OnHeldLiveFeedCountChanged(int value)
    {
        OnPropertyChanged(nameof(RetainedLiveFeedCount));
        OnPropertyChanged(nameof(DroppedHeldLiveFeedCount));
        OnPropertyChanged(nameof(LiveFeedReviewStatus));
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
        ThemeManager.ApplyTextScale(InterfaceTextScalePercent);
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
        OnPropertyChanged(nameof(ModerationLevelThresholdSummary));
        if (_isInitializing) return;
        HasFilterTestResult = false;
        PersistModerationSettings();
        AnnounceState(
            $"Moderation strength changed to {ModerationLevelName}, " +
            $"level {Config.IntentModerationLevel} of 4. " +
            ModerationLevelThresholdSummary);
    }

    partial void OnSelectedModerationModelChanged(ModerationModelPreference value)
    {
        ModerationModelPreference normalized = Enum.IsDefined(value)
            ? value
            : ModerationModelPreference.BuiltInHybrid;
        if (value != normalized)
        {
            SelectedModerationModel = normalized;
            return;
        }

        if (_isInitializing)
        {
            RefreshQwenModelProperties();
            return;
        }

        _settings.ModerationModel = normalized;
        _pipeline.SetIntentClassifier(CreateSelectedIntentClassifier());
        HasFilterTestResult = false;
        SaveSettingsOrReport();
        RefreshQwenModelProperties();
        if (normalized == ModerationModelPreference.Qwen3Guard06BCompressed)
        {
            _ = StartInstalledQwenModelAsync();
        }
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
        _voicePreviewOutput.Volume = Math.Clamp(SpeechVolume, 0, 150);
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
                Platform = liveEvent.Platform,
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

        if (Application.Current?.Dispatcher is not { HasShutdownStarted: false } dispatcher)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            if (_incomingEventCts.IsCancellationRequested)
            {
                return;
            }

            if (IsLiveFeedReviewPaused)
            {
                if (_heldLiveFeedDecisions.Count >= 100)
                {
                    _heldLiveFeedDecisions.RemoveAt(0);
                }

                _heldLiveFeedDecisions.Add(decision);
                HeldLiveFeedCount++;
            }
            else
            {
                AddDecisionToLiveFeed(decision);
            }

            if (decision.Passed && IsArmed &&
                !_ttsQueue.Enqueue(decision, bypassPause))
            {
                if (_ttsQueue.Count >= _ttsQueue.Capacity &&
                    Interlocked.Exchange(ref _queueSaturationAnnounced, 1) == 0)
                {
                    AnnounceState(
                        "The approved message queue is full. Additional messages will be skipped until space is available.");
                }
            }
        });

    }

    private void AddDecisionToLiveFeed(ModerationDecision decision)
    {
        if (LiveFeed.Count >= 100)
        {
            LiveFeed.RemoveAt(LiveFeed.Count - 1);
        }

        LiveFeed.Insert(0, new LiveFeedEntryViewModel(decision));
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
    public async Task SaveAndConnectSource()
    {
        string selectedId = SourceConnectorChoices.Any(choice =>
            string.Equals(choice.Id, SelectedSourceConnectorId, StringComparison.OrdinalIgnoreCase))
            ? SelectedSourceConnectorId
            : TikFinityWebSocketClient.ConnectorDescriptor.Id;
        string username = "";
        if (string.Equals(selectedId, TikTokLiveConnector.ConnectorDescriptor.Id, StringComparison.OrdinalIgnoreCase) &&
            !TikTokLiveConnector.TryNormalizeUsername(TikTokUsername, out username))
        {
            AnnounceState(
                "Enter a valid TikTok username using letters, numbers, periods, or underscores.",
                interrupt: true);
            return;
        }

        Interlocked.Increment(ref _monitoringGeneration);
        _sessionDonors.Clear();
        _ttsQueue.Disarm();
        _auditLogger.EndSession();
        IsConnected = false;
        ConnectionStatusText =
            "Changing live stream source. SafeSpeak is disarmed and the speech queue is clear.";

        try
        {
            await _sourceConnector.ReplaceAsync(() =>
                SourceConnectorRegistry.CreateDefault(username).Create(selectedId));
            _settings.SelectedSourceConnectorId = selectedId;
            _settings.TikTokUsername = username;
            _settings.AutoConnectSource = true;
            TikTokUsername = username;
            SaveSettingsOrReport();
            OnPropertyChanged(nameof(SourceName));
            OnPropertyChanged(nameof(SourceDescription));
            await _sourceConnector.ConnectAsync();
            AnnounceState(
                $"{SourceName} selected. SafeSpeak remains disarmed while the source connects.",
                interrupt: true);
        }
        catch (Exception ex)
        {
            ConnectionStatusText = $"Source change failed. {ex.Message}";
            AnnounceState(ConnectionStatusText, interrupt: true);
        }
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
        LiveStatusAnnouncement = announcement;
        _announcer.AnnounceOnDemand(announcement, interrupt: true);
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
    public void AddCustomAllowedTerm()
    {
        string term = CustomAllowedInput.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            AnnounceState("Type a word or phrase before adding an allowed term.");
            return;
        }

        if (term.Length > 256)
        {
            AnnounceState("Allowed terms can contain at most 256 characters.");
            return;
        }

        string normalizedTerm = UnicodeNormalizer.NormalizeForInspection(term);
        if (_pipeline.Rules.DefaultRules.Contains(normalizedTerm))
        {
            AnnounceState("Built-in severe-abuse safety terms cannot be added to the allowed list.");
            return;
        }

        if (CustomAllowedTerms.Any(existing =>
                string.Equals(existing, term, StringComparison.OrdinalIgnoreCase)))
        {
            AnnounceState("That allowed term is already in the list.");
            return;
        }

        if (CustomAllowedTerms.Count >= 500)
        {
            AnnounceState("The allowed-terms list has reached its limit of 500 entries.");
            return;
        }

        CustomAllowedTerms.Add(term);
        Config.CustomAllowedTerms.Add(term);
        PersistModerationSettings();
        SelectedCustomAllowedTerm = term;
        OnPropertyChanged(nameof(AllowedRulesSummary));
        AnnounceState("Allowed term added. Built-in severe-abuse and contextual safety checks remain active.");
        CustomAllowedInput = "";
    }

    [RelayCommand]
    public void RemoveSelectedCustomAllowedTerm()
    {
        if (string.IsNullOrWhiteSpace(SelectedCustomAllowedTerm))
        {
            AnnounceState("Choose an allowed term to remove.");
            return;
        }

        string term = SelectedCustomAllowedTerm;
        CustomAllowedTerms.Remove(term);
        Config.CustomAllowedTerms.RemoveAll(existing =>
            string.Equals(existing, term, StringComparison.OrdinalIgnoreCase));
        SelectedCustomAllowedTerm = null;
        PersistModerationSettings();
        OnPropertyChanged(nameof(AllowedRulesSummary));
        AnnounceState("Allowed term removed.");
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
            OnPropertyChanged(nameof(IntentModelStatus));
            OnPropertyChanged(nameof(IntentModelShortStatus));
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

        _announcer.StopSpeaking();
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

    private void AnnounceNarration(
        string narration,
        string conciseStatus,
        bool interrupt = false)
    {
        LiveStatusAnnouncement = conciseStatus;
        _announcer.Announce(narration, interrupt);
    }

    [RelayCommand]
    public void TestGuidanceAudio()
    {
        AnnounceNarration(
            $"SafeSpeak built-in guidance is playing through {GuidanceAudioEndpointName} at {ReaderSpeechVolume} percent volume.",
            $"Testing built-in guidance on {GuidanceAudioEndpointName}.",
            interrupt: true);
    }

    private IpcStateBroadcast GetIpcState()
    {
        return new IpcStateBroadcast
        {
            IsArmed = _ttsQueue.IsArmed,
            IsAutoPlay = _ttsQueue.IsAutoPlay,
            IsPaused = _ttsQueue.IsPaused,
            IsSpeaking = _ttsQueue.IsSpeaking,
            QueueCount = _ttsQueue.Count,
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
                    return _ttsQueue.IsArmed ? "Armed" : "Disarmed";

                case "toggle_autoplay":
                    if (!_ttsQueue.IsArmed)
                    {
                        AnnounceState(
                            "SafeSpeak is disarmed. Arm SafeSpeak before choosing automatic playback.");
                        return "Disarmed";
                    }
                    if (_ttsQueue.Mode == TtsPlaybackMode.Automatic)
                    {
                        UseManualPlayback();
                        return "ManualPlaybackEnabled";
                    }
                    UseAutomaticPlayback();
                    return "AutomaticPlaybackEnabled";

                case "toggle_pause":
                    if (!_ttsQueue.IsArmed)
                    {
                        AnnounceState(
                            "SafeSpeak is disarmed. There is no text to speech playback to pause.");
                        return "Disarmed";
                    }
                    PauseOrResumeTts();
                    return _ttsQueue.Mode == TtsPlaybackMode.Paused ? "Paused" : "Resumed";

                case "pause":
                    if (!_ttsQueue.IsArmed)
                    {
                        AnnounceState(
                            "SafeSpeak is disarmed. There is no text to speech playback to pause.");
                        return "Disarmed";
                    }
                    _ttsQueue.SetPaused(true);
                    AnnounceState("TTS Queue Paused");
                    return "Paused";

                case "resume":
                    if (!_ttsQueue.IsArmed)
                    {
                        AnnounceState(
                            "SafeSpeak is disarmed. Arm SafeSpeak before resuming playback.");
                        return "Disarmed";
                    }
                    _ttsQueue.ResumeAutomatic();
                    AnnounceState("TTS Queue Resumed");
                    return "Resumed";

                case "manual":
                    if (!_ttsQueue.IsArmed)
                    {
                        UseManualPlayback();
                        return "Disarmed";
                    }
                    UseManualPlayback();
                    return "ManualPlaybackEnabled";

                case "automatic":
                    if (!_ttsQueue.IsArmed)
                    {
                        UseAutomaticPlayback();
                        return "Disarmed";
                    }
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

                case "stop_guidance":
                    StopBuiltInGuidance();
                    return "BuiltInGuidanceStopped";

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
        _moderationModelInstallCts?.Cancel();
        _qwenRuntime.StopNow();
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
        await IgnoreShutdownFailureAsync(_qwenRuntime.DisposeAsync().AsTask());
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
        _settings.SpeechVolume = Math.Clamp(SpeechVolume, 0, 150);
        _settings.ReaderSpeechRate = Math.Clamp(ReaderSpeechRate, -5, 5);
        _settings.QueueLimit = Math.Clamp(QueueLimit, 1, 500);
        _settings.InterMessageGapMilliseconds = Math.Clamp(InterMessageGapTenths, 0, 50) * 100;
        _settings.AdaptiveInterMessageGap = AdaptiveInterMessageGap;
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
