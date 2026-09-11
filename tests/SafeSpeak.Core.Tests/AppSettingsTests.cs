using System.Text.Json;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void VolumeDefaultsUseOneHundredPercent()
    {
        var settings = new AppSettings();

        Assert.Equal(100, settings.SpeechVolume);
        Assert.Equal(0, settings.SpeechBoost);
        Assert.Equal(100, settings.ReaderSpeechVolume);
        Assert.Equal(
            ModerationModelPreference.BuiltInHybrid,
            settings.ModerationModel);
    }

    [Fact]
    public void OptionalModerationModelChoicePersists()
    {
        var settings = new AppSettings
        {
            ModerationModel = ModerationModelPreference.Qwen3Guard06BCompressed
        };

        string json = JsonSerializer.Serialize(settings);
        AppSettings reloaded = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal(
            ModerationModelPreference.Qwen3Guard06BCompressed,
            reloaded.ModerationModel);
    }

    [Fact]
    public void CreateModerationConfig_CopiesSavedValuesWithoutSharingTheTermList()
    {
        var settings = new AppSettings
        {
            AudienceMode = AudienceMode.SubscribersOnly,
            Strictness = ModerationStrictness.Maximum,
            EnglishOnly = false,
            RejectMixedScripts = false,
            StripUrls = false,
            AllowDonorsToSpeak = false,
            SpeakUsernames = true,
            AiClassificationEnabled = true,
            AiToxicityThreshold = 0.8,
            IntentModerationLevel = 4,
            CustomBlockedTerms = ["blocked phrase"],
            CustomAllowedTerms = ["allowed phrase"]
        };

        ModerationConfig config = settings.CreateModerationConfig();
        config.CustomBlockedTerms.Add("second phrase");
        config.CustomAllowedTerms.Add("second allowed phrase");

        Assert.Equal(AudienceMode.SubscribersOnly, config.AudienceMode);
        Assert.Equal(ModerationStrictness.Maximum, config.Strictness);
        Assert.False(config.EnglishOnly);
        Assert.False(config.RejectMixedScripts);
        Assert.False(config.StripUrls);
        Assert.False(config.AllowDonorsToSpeak);
        Assert.True(config.SpeakUsernames);
        Assert.True(config.AiClassificationEnabled);
        Assert.Equal(0.8, config.AiToxicityThreshold);
        Assert.Equal(4, config.IntentModerationLevel);
        Assert.Single(settings.CustomBlockedTerms);
        Assert.Single(settings.CustomAllowedTerms);
    }

    [Fact]
    public void CaptureModerationConfig_CopiesCurrentValuesAndClampsSensitivity()
    {
        var settings = new AppSettings();
        var config = new ModerationConfig
        {
            AudienceMode = AudienceMode.ModeratorsOnly,
            Strictness = ModerationStrictness.Standard,
            EnglishOnly = false,
            AllowDonorsToSpeak = false,
            AiToxicityThreshold = 2.0,
            IntentModerationLevel = 9,
            CustomBlockedTerms = ["custom"],
            CustomAllowedTerms = ["allowed"]
        };

        settings.CaptureModerationConfig(config);
        config.CustomBlockedTerms.Clear();
        config.CustomAllowedTerms.Clear();

        Assert.Equal(AudienceMode.ModeratorsOnly, settings.AudienceMode);
        Assert.Equal(ModerationStrictness.Standard, settings.Strictness);
        Assert.False(settings.EnglishOnly);
        Assert.False(settings.AllowDonorsToSpeak);
        Assert.Equal(0.95, settings.AiToxicityThreshold);
        Assert.Equal(4, settings.IntentModerationLevel);
        Assert.Equal(["custom"], settings.CustomBlockedTerms);
        Assert.Equal(["allowed"], settings.CustomAllowedTerms);
    }

    [Fact]
    public void LegacyPrivacySettings_CannotOptIntoLoggingOrSecondaryEngines()
    {
        const string legacyJson = """
            {
              "EnableStreamAuditLogging": true,
              "SelectedIntentEngineId": "google_perspective",
              "PerspectiveApiKey": "must-not-load",
              "LocalLlmEndpointUrl": "https://example.com",
              "LocalLlmModelName": "remote-model"
            }
            """;

        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(legacyJson)!;

        Assert.False(settings.EnableStreamAuditLogging);
        Assert.Equal("local_hybrid", settings.SelectedIntentEngineId);
        Assert.Null(settings.PerspectiveApiKey);
        Assert.Equal("http://localhost:11434", settings.LocalLlmEndpointUrl);
        Assert.Equal("llama3.2:1b", settings.LocalLlmModelName);
    }

    [Fact]
    public void CurrentLoggingConsentPersistsWhileSecondaryEnginesRemainSessionOnly()
    {
        var settings = new AppSettings
        {
            EnableStreamAuditLogging = true,
            SelectedIntentEngineId = "google_perspective",
            PerspectiveApiKey = "must-not-save",
            LocalLlmEndpointUrl = "http://localhost:9999",
            LocalLlmModelName = "temporary-model"
        };

        string json = JsonSerializer.Serialize(settings);

        Assert.DoesNotContain("EnableStreamAuditLogging", json);
        Assert.Contains("\"HasConsentedToLocalAuditLogging\":true", json);
        Assert.DoesNotContain("SelectedIntentEngineId", json);
        Assert.DoesNotContain("PerspectiveApiKey", json);
        Assert.DoesNotContain("LocalLlmEndpointUrl", json);
        Assert.DoesNotContain("LocalLlmModelName", json);
        Assert.DoesNotContain("must-not-save", json);
    }

    [Fact]
    public void SpeakerAndPauseRoutingChoicesRoundTrip()
    {
        var settings = new AppSettings
        {
            AudienceMode = AudienceMode.FollowersOnly,
            AllowDonorsToSpeak = true,
            PauseAllTtsWhilePaused = false,
            AllowGiftAnnouncementsWhilePaused = true,
            AllowFollowAnnouncementsWhilePaused = false,
            AllowShareAnnouncementsWhilePaused = true,
            AllowSubscriptionAnnouncementsWhilePaused = false
        };

        string json = JsonSerializer.Serialize(settings);
        AppSettings reloaded = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal(AudienceMode.FollowersOnly, reloaded.AudienceMode);
        Assert.True(reloaded.AllowDonorsToSpeak);
        Assert.False(reloaded.PauseAllTtsWhilePaused);
        Assert.True(reloaded.AllowGiftAnnouncementsWhilePaused);
        Assert.False(reloaded.AllowFollowAnnouncementsWhilePaused);
        Assert.True(reloaded.AllowShareAnnouncementsWhilePaused);
        Assert.False(reloaded.AllowSubscriptionAnnouncementsWhilePaused);
    }

    [Fact]
    public void ConnectorSetsAndGuidePlacementRoundTrip()
    {
        string path = CreateTemporarySettingsPath();
        try
        {
            AppSettings settings = AppSettings.Load(path);
            settings.ConfiguredSourceConnectorIds = ["tikfinity", "tiktok-direct"];
            settings.ActiveSourceConnectorIds = ["tiktok-direct"];
            settings.TikTokUsername = "creator.name";
            settings.SafetyGuideControlsAtTop = false;
            settings.SettingsGuideControlsAtTop = false;

            Assert.True(settings.TrySave(out string? error), error);

            AppSettings reloaded = AppSettings.Load(path);
            Assert.Equal(["tikfinity", "tiktok-direct"], reloaded.ConfiguredSourceConnectorIds);
            Assert.Equal(["tiktok-direct"], reloaded.ActiveSourceConnectorIds);
            Assert.Equal("creator.name", reloaded.TikTokUsername);
            Assert.False(reloaded.SafetyGuideControlsAtTop);
            Assert.False(reloaded.SettingsGuideControlsAtTop);
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Fact]
    public void EmptyConfiguredConnectorSetPersistsForCurrentSchema()
    {
        string path = CreateTemporarySettingsPath();
        try
        {
            var settings = AppSettings.Load(path);
            settings.ConfiguredSourceConnectorIds = [];
            settings.ActiveSourceConnectorIds = [];
            settings.AutoConnectSource = false;

            Assert.True(settings.TrySave(out string? error), error);

            AppSettings reloaded = AppSettings.Load(path);
            Assert.Empty(reloaded.ConfiguredSourceConnectorIds);
            Assert.Empty(reloaded.ActiveSourceConnectorIds);
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Fact]
    public void Load_MigratesLegacySelectedConnectorIntoConfiguredAndActiveSets()
    {
        const string legacyJson = """
            {
              "SettingsSchemaVersion": 9,
              "SelectedSourceConnectorId": "tiktok-live",
              "TikTokUsername": "creator_name",
              "AutoConnectSource": true
            }
            """;
        string path = CreateTemporarySettingsPath();
        try
        {
            WriteSettings(path, legacyJson);

            AppSettings settings = AppSettings.Load(path);

            Assert.Equal("tiktok-direct", settings.SelectedSourceConnectorId);
            Assert.Equal(["tiktok-direct"], settings.ConfiguredSourceConnectorIds);
            Assert.Equal(["tiktok-direct"], settings.ActiveSourceConnectorIds);
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Fact]
    public void AccessibilityPreferences_RoundTripCurrentAndPendingSelections()
    {
        string path = CreateTemporarySettingsPath();
        try
        {
            AppSettings pending = AppSettings.Load(path);
            pending.PendingSpokenGuidance = SpokenGuidanceMode.Enabled;
            pending.PendingTheme = ThemePreference.Dark;

            Assert.True(pending.TrySave(out string? pendingSaveError), pendingSaveError);

            AppSettings reloadedPending = AppSettings.Load(path);
            Assert.Equal(
                SpokenGuidanceMode.Enabled,
                reloadedPending.PendingSpokenGuidance);
            Assert.Equal(ThemePreference.Dark, reloadedPending.PendingTheme);
            Assert.True(reloadedPending.IsAwaitingAccessibilityConfirmation);
            Assert.Equal(
                OnboardingStage.Accessibility,
                reloadedPending.OnboardingStage);
            Assert.False(reloadedPending.HasCompletedOnboarding);

            reloadedPending.SpokenGuidance = SpokenGuidanceMode.Enabled;
            reloadedPending.Theme = ThemePreference.Dark;
            reloadedPending.PendingSpokenGuidance = SpokenGuidanceMode.Unset;
            reloadedPending.PendingTheme = ThemePreference.Unset;
            reloadedPending.OnboardingStage = OnboardingStage.Platform;

            Assert.True(
                reloadedPending.TrySave(out string? confirmedSaveError),
                confirmedSaveError);

            AppSettings reloadedConfirmed = AppSettings.Load(path);
            Assert.Equal(
                AppSettings.CurrentSettingsSchemaVersion,
                reloadedConfirmed.SettingsSchemaVersion);
            Assert.Equal(
                SpokenGuidanceMode.Enabled,
                reloadedConfirmed.SpokenGuidance);
            Assert.Equal(ThemePreference.Dark, reloadedConfirmed.Theme);
            Assert.True(reloadedConfirmed.HasConfirmedAccessibilityPreferences);
            Assert.Equal(
                OnboardingStage.Platform,
                reloadedConfirmed.OnboardingStage);
            Assert.False(reloadedConfirmed.HasCompletedOnboarding);
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Fact]
    public void PendingAccessibilityConfirmation_DoesNotResetCompletedOnboardingOrConnectors()
    {
        string path = CreateTemporarySettingsPath();
        try
        {
            var settings = AppSettings.Load(path);
            settings.OnboardingStage = OnboardingStage.Complete;
            settings.PendingSpokenGuidance = SpokenGuidanceMode.Enabled;
            settings.PendingTheme = ThemePreference.HighContrast;
            settings.ConfiguredSourceConnectorIds = ["tiktok-direct"];
            settings.ActiveSourceConnectorIds = ["tiktok-direct"];
            settings.SelectedSourceConnectorId = "tiktok-direct";
            settings.TikTokUsername = "creator_name";

            Assert.True(settings.TrySave(out string? error), error);

            AppSettings reloaded = AppSettings.Load(path);
            Assert.Equal(OnboardingStage.Complete, reloaded.OnboardingStage);
            Assert.True(reloaded.IsAwaitingAccessibilityConfirmation);
            Assert.True(reloaded.HasCompletedOnboarding);
            Assert.Equal(["tiktok-direct"], reloaded.ConfiguredSourceConnectorIds);
            Assert.Equal(["tiktok-direct"], reloaded.ActiveSourceConnectorIds);
            Assert.Equal("creator_name", reloaded.TikTokUsername);
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Fact]
    public void Load_MigratesConfirmedLegacyProfileAndIndependentHighContrastToggle()
    {
        const string legacyJson = """
            {
              "AccessibilityProfile": 1,
              "PendingAccessibilityProfile": 0,
              "HasCompletedAccessibilitySetup": true,
              "UseHighContrastTheme": true
            }
            """;
        string path = CreateTemporarySettingsPath();
        try
        {
            WriteSettings(path, legacyJson);

            AppSettings settings = AppSettings.Load(path);

            Assert.Equal(SpokenGuidanceMode.Enabled, settings.SpokenGuidance);
            Assert.Equal(ThemePreference.HighContrast, settings.Theme);
            Assert.True(settings.HasConfirmedAccessibilityPreferences);
            Assert.False(settings.IsAwaitingAccessibilityConfirmation);
            Assert.Equal(OnboardingStage.Platform, settings.OnboardingStage);
            Assert.False(settings.HasCompletedOnboarding);
            Assert.Equal(
                AppSettings.CurrentSettingsSchemaVersion,
                settings.SettingsSchemaVersion);

            Assert.True(settings.TrySave(out string? saveError), saveError);
            string migratedJson = File.ReadAllText(path);
            Assert.DoesNotContain("AccessibilityProfile", migratedJson);
            Assert.DoesNotContain("PendingAccessibilityProfile", migratedJson);
            Assert.DoesNotContain("HasCompletedAccessibilitySetup", migratedJson);
            Assert.DoesNotContain("UseHighContrastTheme", migratedJson);
            using JsonDocument document = JsonDocument.Parse(migratedJson);
            Assert.Equal(
                AppSettings.CurrentSettingsSchemaVersion,
                document.RootElement.GetProperty("SettingsSchemaVersion").GetInt32());
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Fact]
    public void Load_MigratesPendingLegacyProfileAsACombinedPendingSelection()
    {
        const string legacyJson = """
            {
              "AccessibilityProfile": 0,
              "PendingAccessibilityProfile": "StandardVisual",
              "HasCompletedAccessibilitySetup": false,
              "UseHighContrastTheme": false
            }
            """;
        string path = CreateTemporarySettingsPath();
        try
        {
            WriteSettings(path, legacyJson);

            AppSettings settings = AppSettings.Load(path);

            Assert.Equal(
                SpokenGuidanceMode.Disabled,
                settings.PendingSpokenGuidance);
            Assert.Equal(ThemePreference.Light, settings.PendingTheme);
            Assert.Equal(SpokenGuidanceMode.Unset, settings.SpokenGuidance);
            Assert.Equal(ThemePreference.Unset, settings.Theme);
            Assert.True(settings.IsAwaitingAccessibilityConfirmation);
            Assert.False(settings.HasConfirmedAccessibilityPreferences);
            Assert.Equal(OnboardingStage.Accessibility, settings.OnboardingStage);
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Fact]
    public void Load_MigratesLegacyThemeWithoutInventingAGuidanceChoice()
    {
        const string legacyJson = """
            {
              "AccessibilityProfile": 0,
              "PendingAccessibilityProfile": 0,
              "HasCompletedAccessibilitySetup": false,
              "UseHighContrastTheme": true
            }
            """;
        string path = CreateTemporarySettingsPath();
        try
        {
            WriteSettings(path, legacyJson);

            AppSettings settings = AppSettings.Load(path);

            Assert.Equal(
                SpokenGuidanceMode.Unset,
                settings.PendingSpokenGuidance);
            Assert.Equal(ThemePreference.HighContrast, settings.PendingTheme);
            Assert.Equal(ThemePreference.HighContrast, settings.EffectiveTheme);
            Assert.False(settings.IsAwaitingAccessibilityConfirmation);
            Assert.False(settings.HasConfirmedAccessibilityPreferences);
            Assert.Equal(OnboardingStage.Accessibility, settings.OnboardingStage);
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Theory]
    [InlineData(OnboardingStage.Accessibility, false)]
    [InlineData(OnboardingStage.Platform, false)]
    [InlineData(OnboardingStage.Filtering, false)]
    [InlineData(OnboardingStage.Review, false)]
    [InlineData(OnboardingStage.Complete, true)]
    public void OnboardingProgress_RoundTripsAndCompletionIsDerived(
        OnboardingStage stage,
        bool expectedCompleted)
    {
        string path = CreateTemporarySettingsPath();
        try
        {
            AppSettings settings = AppSettings.Load(path);
            settings.SpokenGuidance = SpokenGuidanceMode.Enabled;
            settings.Theme = ThemePreference.Dark;
            settings.OnboardingStage = stage;

            Assert.True(settings.TrySave(out string? saveError), saveError);

            AppSettings reloaded = AppSettings.Load(path);
            Assert.Equal(stage, reloaded.OnboardingStage);
            Assert.Equal(expectedCompleted, reloaded.HasCompletedOnboarding);
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Fact]
    public void Load_IncompleteSettingsCannotResumePastAccessibility()
    {
        const string inconsistentJson = """
            {
              "SettingsSchemaVersion": 3,
              "OnboardingStage": 2,
              "SpokenGuidance": 0,
              "Theme": 0
            }
            """;
        string path = CreateTemporarySettingsPath();
        try
        {
            WriteSettings(path, inconsistentJson);

            AppSettings settings = AppSettings.Load(path);

            Assert.Equal(OnboardingStage.Accessibility, settings.OnboardingStage);
            Assert.False(settings.HasCompletedOnboarding);
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Fact]
    public void ObsoleteAccessibilityProfileSymbolsAreNotPublic()
    {
        Type settingsType = typeof(AppSettings);

        Assert.Null(settingsType.GetProperty("AccessibilityProfile"));
        Assert.Null(settingsType.GetProperty("PendingAccessibilityProfile"));
        Assert.Null(settingsType.GetProperty("HasCompletedAccessibilitySetup"));
        Assert.Null(settingsType.GetProperty("UseHighContrastTheme"));
    }

    [Fact]
    public void AllToggleBoxesRoundTripThroughPersistence()
    {
        string path = CreateTemporarySettingsPath();
        try
        {
            var original = new AppSettings();
            original.AttachToPath(path);

            // Invert every toggle from default
            original.EnableStreamAuditLogging = true;
            original.BroadcastOutputEnabled = false;
            original.AdaptiveInterMessageGap = false;
            original.NarrateDetailedHelp = false;
            original.NarrateTypedCharacters = true;
            original.MessageRateLimitEnabled = false;
            original.EnglishOnly = false;
            original.RejectMixedScripts = false;
            original.AllowDonorsToSpeak = false;
            original.AnnounceChatMessages = false;
            original.AnnounceGifts = false;
            original.AnnounceFollows = false;
            original.AnnounceShares = false;
            original.AnnounceSubscriptions = false;
            original.AnnounceJoins = true;
            original.AnnounceLikes = true;
            original.PauseAllTtsWhilePaused = false;
            original.AllowGiftAnnouncementsWhilePaused = false;
            original.AllowFollowAnnouncementsWhilePaused = false;
            original.AllowShareAnnouncementsWhilePaused = false;
            original.AllowSubscriptionAnnouncementsWhilePaused = false;
            original.InstantAlertsGifts = false;
            original.InstantAlertsFollows = false;
            original.InstantAlertsShares = true;
            original.InstantAlertsSubscriptions = true;
            original.InstantAlertsJoins = true;
            original.InstantAlertsLikes = true;
            original.SpokenGuidance = SpokenGuidanceMode.Enabled;
            original.Theme = ThemePreference.Dark;
            original.AutoConnectSource = false;

            Assert.True(original.TrySave(out string? saveError), saveError);

            AppSettings reloaded = AppSettings.Load(path);

            Assert.True(reloaded.EnableStreamAuditLogging);
            Assert.True(reloaded.HasConsentedToLocalAuditLogging);
            Assert.False(reloaded.BroadcastOutputEnabled);
            Assert.False(reloaded.AdaptiveInterMessageGap);
            Assert.False(reloaded.NarrateDetailedHelp);
            Assert.True(reloaded.NarrateTypedCharacters);
            Assert.False(reloaded.MessageRateLimitEnabled);
            Assert.False(reloaded.EnglishOnly);
            Assert.False(reloaded.RejectMixedScripts);
            Assert.False(reloaded.AllowDonorsToSpeak);
            Assert.False(reloaded.AnnounceChatMessages);
            Assert.False(reloaded.AnnounceGifts);
            Assert.False(reloaded.AnnounceFollows);
            Assert.False(reloaded.AnnounceShares);
            Assert.False(reloaded.AnnounceSubscriptions);
            Assert.True(reloaded.AnnounceJoins);
            Assert.True(reloaded.AnnounceLikes);
            Assert.False(reloaded.PauseAllTtsWhilePaused);
            Assert.False(reloaded.AllowGiftAnnouncementsWhilePaused);
            Assert.False(reloaded.AllowFollowAnnouncementsWhilePaused);
            Assert.False(reloaded.AllowShareAnnouncementsWhilePaused);
            Assert.False(reloaded.AllowSubscriptionAnnouncementsWhilePaused);
            Assert.False(reloaded.InstantAlertsGifts);
            Assert.False(reloaded.InstantAlertsFollows);
            Assert.True(reloaded.InstantAlertsShares);
            Assert.True(reloaded.InstantAlertsSubscriptions);
            Assert.True(reloaded.InstantAlertsJoins);
            Assert.True(reloaded.InstantAlertsLikes);
            Assert.Equal(SpokenGuidanceMode.Enabled, reloaded.SpokenGuidance);
            Assert.Equal(ThemePreference.Dark, reloaded.Theme);
            Assert.False(reloaded.AutoConnectSource);
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Fact]
    public void MigrateAuditLoggingConsent_SupportsModernEnableStreamAuditLoggingInJson()
    {
        string path = CreateTemporarySettingsPath();
        try
        {
            WriteSettings(
                path,
                """
                {
                  "SettingsSchemaVersion": 12,
                  "EnableStreamAuditLogging": true
                }
                """);

            AppSettings loaded = AppSettings.Load(path);

            Assert.True(loaded.EnableStreamAuditLogging);
            Assert.True(loaded.HasConsentedToLocalAuditLogging);
        }
        finally
        {
            DeleteTemporarySettingsDirectory(path);
        }
    }

    [Fact]
    public void ObsoletePrivateMonitorSettingsAreNotPublic()
    {
        Type settingsType = typeof(AppSettings);

        Assert.Null(settingsType.GetProperty("PrivateMonitorEnabled"));
        Assert.Null(settingsType.GetProperty("MirrorApprovedMessagesToPrivateMonitor"));
        Assert.Null(settingsType.GetProperty("PrivateModerationNoticesEnabled"));
        Assert.Null(settingsType.GetProperty("SelectedPrivateEndpointId"));
    }

    [Fact]
    public void ShouldPromptSetupUpdate_BehavesCorrectlyAcrossLifecycleAndVersionIncrements()
    {
        // Fresh settings: onboarding is not complete -> do not prompt
        var settings = new AppSettings();
        Assert.False(settings.HasCompletedOnboarding);
        Assert.False(settings.ShouldPromptSetupUpdate);

        // Existing completed profile with default (version 0) acknowledged -> should prompt
        settings.OnboardingStage = OnboardingStage.Complete;
        Assert.True(settings.HasCompletedOnboarding);
        Assert.Equal(0, settings.LastAcknowledgedSetupVersion);
        Assert.True(settings.ShouldPromptSetupUpdate);

        // Awaiting accessibility confirmation takes precedence over prompt
        settings.PendingSpokenGuidance = SpokenGuidanceMode.Enabled;
        settings.PendingTheme = ThemePreference.Light;
        settings.SpokenGuidance = SpokenGuidanceMode.Unset;
        settings.Theme = ThemePreference.Unset;
        Assert.True(settings.IsAwaitingAccessibilityConfirmation);
        Assert.False(settings.ShouldPromptSetupUpdate);

        // Restoring confirmation state
        settings.SpokenGuidance = SpokenGuidanceMode.Enabled;
        settings.Theme = ThemePreference.Light;
        Assert.False(settings.IsAwaitingAccessibilityConfirmation);
        Assert.True(settings.ShouldPromptSetupUpdate);

        // User acknowledges current setup guide version -> should NOT prompt again
        settings.LastAcknowledgedSetupVersion = AppSettings.CurrentSetupGuideVersion;
        Assert.False(settings.ShouldPromptSetupUpdate);

        // Persists and round-trips via JSON serialization
        string json = JsonSerializer.Serialize(settings);
        AppSettings reloaded = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal(AppSettings.CurrentSetupGuideVersion, reloaded.LastAcknowledgedSetupVersion);
        Assert.False(reloaded.ShouldPromptSetupUpdate);
    }

    private static string CreateTemporarySettingsPath() => Path.Combine(
        Path.GetTempPath(),
        "SafeSpeak.Core.Tests",
        Guid.NewGuid().ToString("N"),
        "settings.json");

    private static void WriteSettings(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static void DeleteTemporarySettingsDirectory(string settingsPath)
    {
        string? directory = Path.GetDirectoryName(settingsPath);
        if (directory != null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
