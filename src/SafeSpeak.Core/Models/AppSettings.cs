using System.Text.Json;
using System.Text.Json.Serialization;

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

public enum OnboardingStage
{
    Accessibility = 0,
    Platform = 1,
    Filtering = 2,
    Review = 3,
    Complete = 4
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
    public const int CurrentSettingsSchemaVersion = 5;

    public int SettingsSchemaVersion { get; set; } = CurrentSettingsSchemaVersion;
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

    public AudienceMode AudienceMode { get; set; } = AudienceMode.All;
    public ModerationStrictness Strictness { get; set; } = ModerationStrictness.High;
    public bool EnglishOnly { get; set; } = true;
    public bool RejectMixedScripts { get; set; } = true;
    public bool StripUrls { get; set; } = true;
    public bool AllowDonorsToSpeak { get; set; } = true;
    public bool SpeakUsernames { get; set; } = true;
    public bool AiClassificationEnabled { get; set; } = true;
    public double AiToxicityThreshold { get; set; } = 0.65;
    public int IntentModerationLevel { get; set; } = 3;

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

    public bool BroadcastOutputEnabled { get; set; } = true;
    public bool HasConsentedToLocalAuditLogging { get; set; }

    [JsonIgnore]
    public bool EnableStreamAuditLogging
    {
        get => HasConsentedToLocalAuditLogging;
        set => HasConsentedToLocalAuditLogging = value;
    }
    public string SelectedSourceConnectorId { get; set; } = "tikfinity";
    public bool AutoConnectSource { get; set; } = true;
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
    public string? SelectedVoiceName { get; set; }
    public int SpeechRate { get; set; } = 0;
    public int SpeechVolume { get; set; } = 100;
    public int ReaderSpeechRate { get; set; } = 3;

    public List<string> CustomBlockedTerms { get; set; } = new();

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
        SpeechRate = Math.Clamp(SpeechRate, -5, 5);
        SpeechVolume = Math.Clamp(SpeechVolume, 0, 100);
        ReaderSpeechRate = Math.Clamp(ReaderSpeechRate, -5, 5);
        SpeakUsernames = true;
        AiClassificationEnabled = true;

        if (string.IsNullOrWhiteSpace(SelectedSourceConnectorId) ||
            SelectedSourceConnectorId.Length > 128)
        {
            SelectedSourceConnectorId = "tikfinity";
        }

        NormalizeLocalConnectorDetection();

        CustomBlockedTerms = (CustomBlockedTerms ?? [])
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Where(term => term.Length <= 256)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();
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
        if (root.TryGetProperty(
                nameof(SettingsSchemaVersion),
                out JsonElement schemaElement))
        {
            _ = schemaElement.TryGetInt32(out schemaVersion);
        }

        // Schema 4 and earlier treated logging as a hidden secondary-track
        // setting. Neither its obsolete property nor a prematurely injected
        // current property is evidence of informed consent.
        if (schemaVersion < 5)
        {
            settings.HasConsentedToLocalAuditLogging = false;
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
        SpeakUsernames = true,
        AiClassificationEnabled = true,
        AiToxicityThreshold = Math.Clamp(AiToxicityThreshold, 0.3, 0.95),
        IntentModerationLevel = Math.Clamp(IntentModerationLevel, 1, 4),
        CustomBlockedTerms = new List<string>(CustomBlockedTerms)
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
        SpeakUsernames = true;
        AiClassificationEnabled = true;
        AiToxicityThreshold = Math.Clamp(config.AiToxicityThreshold, 0.3, 0.95);
        IntentModerationLevel = Math.Clamp(config.IntentModerationLevel, 1, 4);
        CustomBlockedTerms = new List<string>(config.CustomBlockedTerms);
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
}
