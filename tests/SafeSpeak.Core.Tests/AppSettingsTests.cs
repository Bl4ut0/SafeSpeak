using System.Text.Json;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class AppSettingsTests
{
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
            CustomBlockedTerms = ["blocked phrase"]
        };

        ModerationConfig config = settings.CreateModerationConfig();
        config.CustomBlockedTerms.Add("second phrase");

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
            CustomBlockedTerms = ["custom"]
        };

        settings.CaptureModerationConfig(config);
        config.CustomBlockedTerms.Clear();

        Assert.Equal(AudienceMode.ModeratorsOnly, settings.AudienceMode);
        Assert.Equal(ModerationStrictness.Standard, settings.Strictness);
        Assert.False(settings.EnglishOnly);
        Assert.False(settings.AllowDonorsToSpeak);
        Assert.Equal(0.95, settings.AiToxicityThreshold);
        Assert.Equal(4, settings.IntentModerationLevel);
        Assert.Equal(["custom"], settings.CustomBlockedTerms);
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
    public void ObsoletePrivateMonitorSettingsAreNotPublic()
    {
        Type settingsType = typeof(AppSettings);

        Assert.Null(settingsType.GetProperty("PrivateMonitorEnabled"));
        Assert.Null(settingsType.GetProperty("MirrorApprovedMessagesToPrivateMonitor"));
        Assert.Null(settingsType.GetProperty("PrivateModerationNoticesEnabled"));
        Assert.Null(settingsType.GetProperty("SelectedPrivateEndpointId"));
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
