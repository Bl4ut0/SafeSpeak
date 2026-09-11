using System.Text.Json;
using System.Text.Json.Serialization;
using SafeSpeak.Core.Accessibility;

namespace SafeSpeak.Core.Models;

public enum SpokenGuidanceMode
{
    Unset = 0,
    Disabled = 1,
    Enabled = 2
}

public enum ThemePreference
{
    Unset = 0,
    Light = 1,
    Dark = 2,
    HighContrast = 3
}

public enum ModerationModelPreference
{
    BuiltInHybrid = 0,
    Qwen3Guard06BCompressed = 1
}

public enum OnboardingStage
{
    Accessibility = 0,
    Platform = 1,
    Filtering = 2,
    Review = 3,
    Complete = 4,
    Voice = 5,
    Keybinds = 6,
    Navigation = 7
}

public enum OnboardingConnectorDetectionStatus
{
    NotChecked = 0,
    Detected = 1,
    NotDetected = 2,
    TimedOut = 3,
    Failed = 4
}

public sealed class AppSettings
{
    public const int CurrentSettingsSchemaVersion = 12;
    public const int CurrentSetupGuideVersion = 1;

    public int SettingsSchemaVersion { get; set; } = CurrentSettingsSchemaVersion;
    public int LastAcknowledgedSetupVersion { get; set; } = 0;
    public OnboardingStage OnboardingStage { get; set; } = OnboardingStage.Accessibility;
    public SpokenGuidanceMode SpokenGuidance { get; set; } = SpokenGuidanceMode.Unset;
    public ThemePreference Theme { get; set; } = ThemePreference.Unset;
    public SpokenGuidanceMode PendingSpokenGuidance { get; set; } = SpokenGuidanceMode.Unset;
    public ThemePreference PendingTheme { get; set; } = ThemePreference.Unset;

    [JsonIgnore]
    public bool HasConfirmedAccessibilityPreferences =>
        SpokenGuidance != SpokenGuidanceMode.Unset && Theme != ThemePreference.Unset;

    [JsonIgnore]
    public bool IsAwaitingAccessibilityConfirmation =>
        !HasConfirmedAccessibilityPreferences &&
        PendingSpokenGuidance != SpokenGuidanceMode.Unset &&
        PendingTheme != ThemePreference.Unset;

    [JsonIgnore]
    public SpokenGuidanceMode EffectiveSpokenGuidance =>
        HasConfirmedAccessibilityPreferences
            ? SpokenGuidance
            : PendingSpokenGuidance != SpokenGuidanceMode.Unset
                ? PendingSpokenGuidance
                : SpokenGuidance;

    [JsonIgnore]
    public ThemePreference EffectiveTheme =>
        HasConfirmedAccessibilityPreferences
            ? Theme
            : PendingTheme != ThemePreference.Unset
                ? PendingTheme
                : Theme;

    [JsonIgnore]
    public bool IsSpokenGuidanceEnabled => EffectiveSpokenGuidance == SpokenGuidanceMode.Enabled;

    [JsonIgnore]
    public bool HasCompletedOnboarding => OnboardingStage == OnboardingStage.Complete;

    [JsonIgnore]
    public bool ShouldPromptSetupUpdate =>
        HasCompletedOnboarding &&
        !IsAwaitingAccessibilityConfirmation &&
        LastAcknowledgedSetupVersion < CurrentSetupGuideVersion;

    public AudienceMode AudienceMode { get; set; } = AudienceMode.All;
    public ModerationStrictness Strictness { get; set; } = ModerationStrictness.High;
    public bool EnglishOnly { get; set; } = true;
    public bool RejectMixedScripts { get; set; } = true;
    public bool StripUrls { get; set; } = true;
    public bool AllowDonorsToSpeak { get; set; } = true;
    public bool IgnoreChatReplies { get; set; } = true;
    public bool SpeakUsernames { get; set; } = true;
    public bool AiClassificationEnabled { get; set; } = true;
    public double AiToxicityThreshold { get; set; } = 0.65;
    public int IntentModerationLevel { get; set; } = 3;
    public ModerationModelPreference ModerationModel { get; set; } =
        ModerationModelPreference.BuiltInHybrid;

    public bool AnnounceChatMessages { get; set; } = true;
    public bool AnnounceGifts { get; set; } = true;
    public bool AnnounceFollows { get; set; } = true;
    public bool AnnounceShares { get; set; } = true;
    public bool AnnounceSubscriptions { get; set; } = true;
    public bool AnnounceJoins { get; set; } = false;
    public bool AnnounceLikes { get; set; } = false;
    public bool PauseAllTtsWhilePaused { get; set; } = true;
    public bool AllowGiftAnnouncementsWhilePaused { get; set; } = true;
    public bool AllowFollowAnnouncementsWhilePaused { get; set; } = true;
    public bool AllowShareAnnouncementsWhilePaused { get; set; } = true;
    public bool AllowSubscriptionAnnouncementsWhilePaused { get; set; } = true;
    public bool InstantAlertsGifts { get; set; } = true;
    public bool InstantAlertsFollows { get; set; } = true;
    public bool InstantAlertsShares { get; set; } = false;
    public bool InstantAlertsSubscriptions { get; set; } = false;
    public bool InstantAlertsJoins { get; set; } = false;
    public bool InstantAlertsLikes { get; set; } = false;

    public bool BroadcastOutputEnabled { get; set; } = true;
    public bool HasConsentedToLocalAuditLogging { get; set; } = true;

    [JsonIgnore]
    public bool EnableStreamAuditLogging
    {
        get => HasConsentedToLocalAuditLogging;
        set => HasConsentedToLocalAuditLogging = value;
    }
    public string SelectedSourceConnectorId { get; set; } = "";
    public List<string> ConfiguredSourceConnectorIds { get; set; } = [];
    public List<string> ActiveSourceConnectorIds { get; set; } = [];
    public string TikTokUsername { get; set; } = "";
    public bool AutoConnectSource { get; set; } = true;
    public bool SafetyGuideControlsAtTop { get; set; } = true;
    public bool SettingsGuideControlsAtTop { get; set; } = true;
    public bool LocalConnectorAutoDetectConsent { get; set; }
    public OnboardingConnectorDetectionStatus LocalConnectorDetectionStatus { get; set; } =
        OnboardingConnectorDetectionStatus.NotChecked;
    public string LocalConnectorDetectionSummary { get; set; } =
        "Local connector detection was not requested.";

    // Secondary-track intent engines are session-only until their consent,
    // credential-storage, and failover UX is complete.
    [JsonIgnore]
    public string SelectedIntentEngineId { get; set; } = "local_hybrid";

    [JsonIgnore]
    public string? PerspectiveApiKey { get; set; }

    [JsonIgnore]
    public string LocalLlmEndpointUrl { get; set; } = "http://localhost:11434";

    [JsonIgnore]
    public string LocalLlmModelName { get; set; } = "llama3.2:1b";

    [JsonIgnore]
    public static string AuditLogsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SafeSpeak",
        "Logs"
    );

    public string? SelectedAudioEndpointId { get; set; }
    public string? SelectedBroadcastEndpointId { get; set; }
    public string? SelectedGuidanceAudioEndpointId { get; set; }
    public string? SelectedVoiceName { get; set; }
    public int SpeechRate { get; set; } = 0;
    public int SpeechVolume { get; set; } = 100;
    public int SpeechBoost { get; set; } = 0;
    public int ReaderSpeechRate { get; set; } = 3;
    public int ReaderSpeechVolume { get; set; } = 100;
    public bool NarrateDetailedHelp { get; set; } = true;
    public bool NarrateTypedCharacters { get; set; }
    public int InterfaceTextScalePercent { get; set; } = 100;
    public int QueueLimit { get; set; } = 50;
    public int InterMessageGapMilliseconds { get; set; }
    public bool AdaptiveInterMessageGap { get; set; } = true;
    public bool MessageRateLimitEnabled { get; set; } = true;
    public MessageRateWindow MessageRateWindow { get; set; } = MessageRateWindow.TenSeconds;
    public int PerUserMessageLimit { get; set; } = 3;
    public int StreamMessageLimit { get; set; } = 500;

    public List<GlobalShortcutBinding> GlobalShortcuts { get; set; } =
        GlobalShortcutCatalog.CreateDefaults();

    public List<string> CustomBlockedTerms { get; set; } = new();
    public List<string> CustomAllowedTerms { get; set; } = new();

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SafeSpeak",
        "settings.json"
    );

    private string _settingsFilePath = SettingsFilePath;

    [JsonIgnore]
    internal bool WasRecoveredFromBackup { get; set; }

    public static AppSettings Load() => Load(SettingsFilePath);

    public static AppSettings Load(string settingsFilePath) =>
        new SettingsStore(settingsFilePath).Load();

    internal static AppSettings CreateDefaultForPath(string settingsFilePath)
    {
        var settings = new AppSettings();
        settings.AttachToPath(settingsFilePath);
        return settings;
    }

    internal void AttachToPath(string settingsFilePath)
    {
        _settingsFilePath = Path.GetFullPath(settingsFilePath);
    }

    internal void ApplyMigrations(JsonElement root)
    {
        MigrateLegacyAccessibilitySettings(root, this);
        MigrateAndValidateOnboardingStage(root, this);
        MigrateAuditLoggingConsent(root, this);
        MigrateGlobalShortcuts(root, this);
        MigrateConnectorCollections(root, this);
    }

    internal void NormalizeForPersistence()
    {
        if (!Enum.IsDefined(SpokenGuidance))
        {
            SpokenGuidance = SpokenGuidanceMode.Unset;
        }

        if (!Enum.IsDefined(Theme))
        {
            Theme = ThemePreference.Unset;
        }

        if (!Enum.IsDefined(PendingSpokenGuidance))
        {
            PendingSpokenGuidance = SpokenGuidanceMode.Unset;
        }

        if (!Enum.IsDefined(PendingTheme))
        {
            PendingTheme = ThemePreference.Unset;
        }

        if (!Enum.IsDefined(OnboardingStage) ||
            (!HasConfirmedAccessibilityPreferences &&
             !IsAwaitingAccessibilityConfirmation &&
             OnboardingStage != OnboardingStage.Accessibility))
        {
            OnboardingStage = OnboardingStage.Accessibility;
        }

        if (!Enum.IsDefined(AudienceMode))
        {
            AudienceMode = AudienceMode.All;
        }

        if (!Enum.IsDefined(Strictness))
        {
            Strictness = ModerationStrictness.High;
        }

        if (!double.IsFinite(AiToxicityThreshold))
        {
            AiToxicityThreshold = 0.65;
        }
        AiToxicityThreshold = Math.Clamp(AiToxicityThreshold, 0.3, 0.95);
        IntentModerationLevel = Math.Clamp(IntentModerationLevel, 1, 4);
        if (!Enum.IsDefined(ModerationModel))
        {
            ModerationModel = ModerationModelPreference.BuiltInHybrid;
        }
        SpeechRate = Math.Clamp(SpeechRate, -5, 5);
        if (SpeechVolume > 100 && SpeechBoost == 0)
        {
            SpeechBoost = Math.Clamp(SpeechVolume - 100, 0, 100);
            SpeechVolume = 100;
        }
        else
        {
            SpeechVolume = Math.Clamp(SpeechVolume, 0, 100);
            SpeechBoost = Math.Clamp(SpeechBoost, 0, 100);
        }
        ReaderSpeechRate = Math.Clamp(ReaderSpeechRate, -5, 5);
        ReaderSpeechVolume = Math.Clamp(ReaderSpeechVolume, 0, 150);
        InterfaceTextScalePercent = Math.Clamp(InterfaceTextScalePercent, 100, 200);
        QueueLimit = Math.Clamp(QueueLimit, 1, 500);
        InterMessageGapMilliseconds = Math.Clamp(InterMessageGapMilliseconds, 0, 5000);
        if (!Enum.IsDefined(MessageRateWindow))
        {
            MessageRateWindow = MessageRateWindow.TenSeconds;
        }
        PerUserMessageLimit = Math.Clamp(PerUserMessageLimit, 1, 100);
        StreamMessageLimit = Math.Clamp(StreamMessageLimit, 10, 5000);
        GlobalShortcuts = GlobalShortcutCatalog.NormalizeBindings(GlobalShortcuts);
        SpeakUsernames = true;
        AiClassificationEnabled = true;

        SelectedSourceConnectorId = NormalizeConnectorId(SelectedSourceConnectorId)
            ?? "tikfinity";

        TikTokUsername = Connectors.TikTokLiveConnector.TryNormalizeUsername(TikTokUsername, out string username)
            ? username : "";
        ConfiguredSourceConnectorIds = NormalizeConnectorIds(
            ConfiguredSourceConnectorIds);
        ActiveSourceConnectorIds = NormalizeConnectorIds(
                ActiveSourceConnectorIds)
            .Where(id => ConfiguredSourceConnectorIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        NormalizeLocalConnectorDetection();

        CustomBlockedTerms = (CustomBlockedTerms ?? [])
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Where(term => term.Length <= 256)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();
        CustomAllowedTerms = (CustomAllowedTerms ?? [])
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Where(term => term.Length <= 256)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();
    }

    private static List<string> NormalizeConnectorIds(
        IEnumerable<string>? connectorIds)
    {
        string[] supportedIds = ["tikfinity", "tiktok-direct"];
        List<string> normalized = (connectorIds ?? [])
            .Select(NormalizeConnectorId)
            .Where(id => id is not null)
            .Cast<string>()
            .Where(id => supportedIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized;
    }

    private static string? NormalizeConnectorId(string? connectorId)
    {
        string normalized = (connectorId ?? string.Empty).Trim();
        if (normalized.Equals("tiktok-live", StringComparison.OrdinalIgnoreCase))
            return "tiktok-direct";
        if (normalized.Equals("tiktok-direct", StringComparison.OrdinalIgnoreCase))
            return "tiktok-direct";
        if (normalized.Equals("tikfinity", StringComparison.OrdinalIgnoreCase))
            return "tikfinity";
        return null;
    }

    private void NormalizeLocalConnectorDetection()
    {
        if (!Enum.IsDefined(LocalConnectorDetectionStatus) ||
            !LocalConnectorAutoDetectConsent)
        {
            LocalConnectorDetectionStatus =
                OnboardingConnectorDetectionStatus.NotChecked;
            LocalConnectorDetectionSummary =
                "Local connector detection was not requested.";
            return;
        }

        string normalizedSummary = (LocalConnectorDetectionSummary ?? string.Empty)
            .Trim();
        string expectedSummary = LocalConnectorDetectionStatus switch
        {
            OnboardingConnectorDetectionStatus.Detected =>
                "TikFinity appears to be available on this computer.",
            OnboardingConnectorDetectionStatus.NotDetected =>
                "TikFinity was not detected. You can still select it and connect later.",
            OnboardingConnectorDetectionStatus.TimedOut =>
                "TikFinity detection timed out. You can configure it manually.",
            OnboardingConnectorDetectionStatus.Failed =>
                "TikFinity could not be checked. You can configure it manually.",
            _ => "Local connector detection was not requested."
        };

        // Detection summaries are later announced and must never become a
        // persistence channel for raw process, listener, or exception details.
        // Only the detector's bounded SafeDescription is retained.
        LocalConnectorDetectionSummary =
            normalizedSummary.Length <= 512 &&
            string.Equals(normalizedSummary, expectedSummary, StringComparison.Ordinal)
                ? normalizedSummary
                : expectedSummary;
    }

    private static void MigrateAndValidateOnboardingStage(
        JsonElement root,
        AppSettings settings)
    {
        bool hasPersistedStage = root.TryGetProperty(
            "OnboardingStage",
            out _);

        if (!hasPersistedStage)
        {
            // Version 2 and legacy settings did not store wizard progress.
            // A confirmed accessibility combination has already completed the
            // first step, so resume at Platform rather than asking it again.
            settings.OnboardingStage = settings.HasConfirmedAccessibilityPreferences
                ? OnboardingStage.Platform
                : OnboardingStage.Accessibility;
            return;
        }

        if (
            !Enum.IsDefined(settings.OnboardingStage) ||
            (
                !settings.HasConfirmedAccessibilityPreferences &&
                !settings.IsAwaitingAccessibilityConfirmation &&
                settings.OnboardingStage != OnboardingStage.Accessibility
            ))
        {
            settings.OnboardingStage = OnboardingStage.Accessibility;
        }
    }

    private static void MigrateAuditLoggingConsent(
        JsonElement root,
        AppSettings settings)
    {
        int schemaVersion = 0;
        if (root.TryGetProperty(nameof(SettingsSchemaVersion), out JsonElement schemaElement))
        {
            _ = schemaElement.TryGetInt32(out schemaVersion);
        }

        if (schemaVersion < 5)
        {
            settings.HasConsentedToLocalAuditLogging = false;
        }
        else if (TryReadBoolean(root, nameof(EnableStreamAuditLogging), out bool enableLogging))
        {
            settings.HasConsentedToLocalAuditLogging = enableLogging;
        }
        else if (TryReadBoolean(root, nameof(HasConsentedToLocalAuditLogging), out bool hasConsented))
        {
            settings.HasConsentedToLocalAuditLogging = hasConsented;
        }
        else
        {
            // Logging is automatic by default for modern schemas
            settings.HasConsentedToLocalAuditLogging = true;
        }
    }

    private static void MigrateGlobalShortcuts(
        JsonElement root,
        AppSettings settings)
    {
        if (!root.TryGetProperty(nameof(GlobalShortcuts), out _))
        {
            settings.GlobalShortcuts = GlobalShortcutCatalog.CreateDefaults();
        }
    }

    private static void MigrateConnectorCollections(
        JsonElement root,
        AppSettings settings)
    {
        int schemaVersion = 0;
        if (root.TryGetProperty(nameof(SettingsSchemaVersion), out JsonElement schemaElement))
        {
            _ = schemaElement.TryGetInt32(out schemaVersion);
        }

        bool hasConfiguredCollection = root.TryGetProperty(
            nameof(ConfiguredSourceConnectorIds),
            out _);
        if (hasConfiguredCollection &&
            (schemaVersion >= 12 || (settings.ConfiguredSourceConnectorIds?.Count ?? 0) > 0))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.SelectedSourceConnectorId))
        {
            string legacyId = settings.SelectedSourceConnectorId;
            settings.ConfiguredSourceConnectorIds = [legacyId];
            settings.ActiveSourceConnectorIds = settings.AutoConnectSource
                ? [legacyId]
                : [];
        }
        else
        {
            settings.ConfiguredSourceConnectorIds = [];
            settings.ActiveSourceConnectorIds = [];
        }
    }

    private static void MigrateLegacyAccessibilitySettings(
        JsonElement root,
        AppSettings settings)
    {
        bool hasLegacyCurrent = TryReadLegacyProfile(
            root,
            "AccessibilityProfile",
            out LegacyAccessibilityProfile legacyCurrent);
        bool hasLegacyPending = TryReadLegacyProfile(
            root,
            "PendingAccessibilityProfile",
            out LegacyAccessibilityProfile legacyPending);
        bool hasLegacyCompletion = TryReadBoolean(
            root,
            "HasCompletedAccessibilitySetup",
            out bool legacyCompleted);
        bool hasLegacyHighContrast = TryReadBoolean(
            root,
            "UseHighContrastTheme",
            out bool legacyHighContrast);

        LegacyAccessibilitySelection? currentSelection =
            hasLegacyCurrent ? MapLegacyProfile(legacyCurrent) : null;
        LegacyAccessibilitySelection? pendingSelection =
            hasLegacyPending ? MapLegacyProfile(legacyPending) : null;

        bool currentWasConfirmed =
            currentSelection.HasValue && (!hasLegacyCompletion || legacyCompleted);

        if (!currentWasConfirmed)
        {
            pendingSelection ??= currentSelection;
            currentSelection = null;
        }
        else
        {
            // A completed legacy setup never had a meaningful pending choice.
            pendingSelection = null;
        }

        ThemePreference? themeWithoutLegacyProfile = null;
        if (hasLegacyHighContrast)
        {
            if (legacyHighContrast)
            {
                if (currentSelection is { } current)
                {
                    currentSelection = current with { Theme = ThemePreference.HighContrast };
                }
                else if (pendingSelection is { } pending)
                {
                    pendingSelection = pending with { Theme = ThemePreference.HighContrast };
                }
                else
                {
                    themeWithoutLegacyProfile = ThemePreference.HighContrast;
                }
            }
            else if (!currentSelection.HasValue && !pendingSelection.HasValue)
            {
                themeWithoutLegacyProfile = ThemePreference.Light;
            }
        }

        if (currentSelection is { } confirmed)
        {
            if (settings.SpokenGuidance == SpokenGuidanceMode.Unset)
            {
                settings.SpokenGuidance = confirmed.SpokenGuidance;
            }

            if (settings.Theme == ThemePreference.Unset)
            {
                settings.Theme = confirmed.Theme;
            }
        }

        if (pendingSelection is { } pendingChoice)
        {
            if (settings.PendingSpokenGuidance == SpokenGuidanceMode.Unset)
            {
                settings.PendingSpokenGuidance = pendingChoice.SpokenGuidance;
            }

            if (settings.PendingTheme == ThemePreference.Unset)
            {
                settings.PendingTheme = pendingChoice.Theme;
            }
        }
        else if (
            themeWithoutLegacyProfile.HasValue &&
            !settings.HasConfirmedAccessibilityPreferences &&
            settings.PendingTheme == ThemePreference.Unset)
        {
            // Preserve the old visual preference without inventing a guidance
            // choice. Onboarding will still require the complete combination.
            settings.PendingTheme = themeWithoutLegacyProfile.Value;
        }
    }

    private static LegacyAccessibilitySelection? MapLegacyProfile(
        LegacyAccessibilityProfile profile) => profile switch
    {
        LegacyAccessibilityProfile.FullVoiceGuided => new(
            SpokenGuidanceMode.Enabled,
            ThemePreference.Light),
        LegacyAccessibilityProfile.HighContrastVisual => new(
            SpokenGuidanceMode.Disabled,
            ThemePreference.HighContrast),
        LegacyAccessibilityProfile.StandardVisual => new(
            SpokenGuidanceMode.Disabled,
            ThemePreference.Light),
        _ => null
    };

    private static bool TryReadLegacyProfile(
        JsonElement root,
        string propertyName,
        out LegacyAccessibilityProfile profile)
    {
        profile = LegacyAccessibilityProfile.Unset;
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int numericValue))
        {
            profile = (LegacyAccessibilityProfile)numericValue;
            return Enum.IsDefined(profile);
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            if (int.TryParse(text, out numericValue))
            {
                profile = (LegacyAccessibilityProfile)numericValue;
                return Enum.IsDefined(profile);
            }

            return Enum.TryParse(text, ignoreCase: true, out profile) &&
                Enum.IsDefined(profile);
        }

        return false;
    }

    private static bool TryReadBoolean(
        JsonElement root,
        string propertyName,
        out bool value)
    {
        value = false;
        if (!root.TryGetProperty(propertyName, out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        return false;
    }

    private enum LegacyAccessibilityProfile
    {
        Unset = 0,
        FullVoiceGuided = 1,
        HighContrastVisual = 2,
        StandardVisual = 3
    }

    private readonly record struct LegacyAccessibilitySelection(
        SpokenGuidanceMode SpokenGuidance,
        ThemePreference Theme);

    public ModerationConfig CreateModerationConfig() => new()
    {
        AudienceMode = AudienceMode,
        Strictness = Strictness,
        EnglishOnly = EnglishOnly,
        RejectMixedScripts = RejectMixedScripts,
        StripUrls = StripUrls,
        AllowDonorsToSpeak = AllowDonorsToSpeak,
        IgnoreChatReplies = IgnoreChatReplies,
        StreamerUsername = TikTokUsername,
        SpeakUsernames = true,
        AiClassificationEnabled = true,
        AiToxicityThreshold = Math.Clamp(AiToxicityThreshold, 0.3, 0.95),
        IntentModerationLevel = Math.Clamp(IntentModerationLevel, 1, 4),
        UserCooldownSeconds = 0,
        MessageRateLimitEnabled = MessageRateLimitEnabled,
        MessageRateWindow = MessageRateWindow,
        PerUserMessageLimit = Math.Clamp(PerUserMessageLimit, 1, 100),
        StreamMessageLimit = Math.Clamp(StreamMessageLimit, 10, 5000),
        CustomBlockedTerms = new List<string>(CustomBlockedTerms),
        CustomAllowedTerms = new List<string>(CustomAllowedTerms)
    };

    public void CaptureModerationConfig(ModerationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        AudienceMode = config.AudienceMode;
        Strictness = config.Strictness;
        EnglishOnly = config.EnglishOnly;
        RejectMixedScripts = config.RejectMixedScripts;
        StripUrls = config.StripUrls;
        AllowDonorsToSpeak = config.AllowDonorsToSpeak;
        IgnoreChatReplies = config.IgnoreChatReplies;
        SpeakUsernames = true;
        AiClassificationEnabled = true;
        AiToxicityThreshold = Math.Clamp(config.AiToxicityThreshold, 0.3, 0.95);
        IntentModerationLevel = Math.Clamp(config.IntentModerationLevel, 1, 4);
        MessageRateLimitEnabled = config.MessageRateLimitEnabled;
        MessageRateWindow = Enum.IsDefined(config.MessageRateWindow)
            ? config.MessageRateWindow
            : MessageRateWindow.TenSeconds;
        PerUserMessageLimit = Math.Clamp(config.PerUserMessageLimit, 1, 100);
        StreamMessageLimit = Math.Clamp(config.StreamMessageLimit, 10, 5000);
        CustomBlockedTerms = new List<string>(config.CustomBlockedTerms);
        CustomAllowedTerms = new List<string>(config.CustomAllowedTerms);
    }

    public bool TrySave(out string? error)
        => new SettingsStore(_settingsFilePath).TrySave(this, out error);

    public void Save()
    {
        _ = TrySave(out _);
    }

    public void ResetOnboarding()
    {
        OnboardingStage = OnboardingStage.Accessibility;
        SpokenGuidance = SpokenGuidanceMode.Unset;
        Theme = ThemePreference.Unset;
        PendingSpokenGuidance = SpokenGuidanceMode.Unset;
        PendingTheme = ThemePreference.Unset;
    }

    public void ResetIncompleteOnboarding()
    {
        ResetOnboarding();
        SelectedSourceConnectorId = string.Empty;
        ConfiguredSourceConnectorIds = [];
        ActiveSourceConnectorIds = [];
        AutoConnectSource = false;
        TikTokUsername = string.Empty;
        LocalConnectorAutoDetectConsent = false;
        LocalConnectorDetectionStatus = OnboardingConnectorDetectionStatus.NotChecked;
        LocalConnectorDetectionSummary = "Local connector detection is not used during setup.";
        SelectedVoiceName = string.Empty;
        ModerationModel = ModerationModelPreference.BuiltInHybrid;
        AiClassificationEnabled = true;
        IgnoreChatReplies = true;
        GlobalShortcuts = GlobalShortcutCatalog.CreateDefaults();
    }
}
