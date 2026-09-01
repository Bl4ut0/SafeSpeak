using System.Text.Json;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void RoundTrip_UsesCustomPathAndCurrentSchema()
    {
        using var directory = new TemporarySettingsDirectory();
        var store = new SettingsStore(directory.SettingsPath);
        var settings = new AppSettings
        {
            SpokenGuidance = SpokenGuidanceMode.Enabled,
            Theme = ThemePreference.Dark,
            OnboardingStage = OnboardingStage.Complete,
            SpeechVolume = 73,
            CustomBlockedTerms = ["first", "second"]
        };

        Assert.True(store.TrySave(settings, out string? error), error);
        AppSettings loaded = store.Load();

        Assert.Equal(SpokenGuidanceMode.Enabled, loaded.SpokenGuidance);
        Assert.Equal(ThemePreference.Dark, loaded.Theme);
        Assert.Equal(OnboardingStage.Complete, loaded.OnboardingStage);
        Assert.Equal(73, loaded.SpeechVolume);
        Assert.Equal(["first", "second"], loaded.CustomBlockedTerms);
        Assert.Equal(
            AppSettings.CurrentSettingsSchemaVersion,
            loaded.SettingsSchemaVersion);
        Assert.True(File.Exists(directory.SettingsPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void RoundTrip_PreservesExplicitCurrentAuditLoggingConsent()
    {
        using var directory = new TemporarySettingsDirectory();
        var store = new SettingsStore(directory.SettingsPath);
        var settings = new AppSettings
        {
            EnableStreamAuditLogging = true
        };

        Assert.True(store.TrySave(settings, out string? error), error);
        AppSettings loaded = store.Load();

        Assert.True(loaded.EnableStreamAuditLogging);
        Assert.True(loaded.HasConsentedToLocalAuditLogging);
        string json = File.ReadAllText(directory.SettingsPath);
        Assert.Contains("HasConsentedToLocalAuditLogging", json);
        Assert.DoesNotContain("EnableStreamAuditLogging", json);
    }

    [Fact]
    public void Load_PreConsentSchemaCannotSilentlyEnableAuditLogging()
    {
        using var directory = new TemporarySettingsDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(
            directory.SettingsPath,
            """
            {
              "SettingsSchemaVersion": 4,
              "EnableStreamAuditLogging": true,
              "HasConsentedToLocalAuditLogging": true
            }
            """);

        AppSettings loaded = new SettingsStore(directory.SettingsPath).Load();

        Assert.False(loaded.EnableStreamAuditLogging);
        Assert.False(loaded.HasConsentedToLocalAuditLogging);
    }

    [Fact]
    public void Load_CorruptPrimaryRecoversPreviousValidBackup()
    {
        using var directory = new TemporarySettingsDirectory();
        var store = new SettingsStore(directory.SettingsPath);
        var settings = new AppSettings { SpeechVolume = 41 };
        Assert.True(store.TrySave(settings, out string? firstError), firstError);

        settings.SpeechVolume = 82;
        Assert.True(store.TrySave(settings, out string? secondError), secondError);
        Assert.True(File.Exists(store.BackupFilePath));
        File.WriteAllText(directory.SettingsPath, "{ broken json");

        AppSettings recovered = store.Load();

        Assert.Equal(41, recovered.SpeechVolume);
        recovered.SpeechVolume = 55;
        Assert.True(store.TrySave(recovered, out string? repairError), repairError);
        Assert.Equal(55, store.Load().SpeechVolume);
        Assert.Equal(41, AppSettings.Load(store.BackupFilePath).SpeechVolume);
    }

    [Fact]
    public void Load_CorruptPrimaryAndBackupReturnsSafeDefaults()
    {
        using var directory = new TemporarySettingsDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(directory.SettingsPath, "not json");
        File.WriteAllText(directory.SettingsPath + ".bak", "also not json");

        AppSettings loaded = new SettingsStore(directory.SettingsPath).Load();

        Assert.Equal(ThemePreference.Unset, loaded.Theme);
        Assert.Equal(SpokenGuidanceMode.Unset, loaded.SpokenGuidance);
        Assert.Equal(OnboardingStage.Accessibility, loaded.OnboardingStage);
        Assert.False(loaded.EnableStreamAuditLogging);
    }

    [Fact]
    public void Load_PartialSettingsNormalizesUnsafeValues()
    {
        using var directory = new TemporarySettingsDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(
            directory.SettingsPath,
            """
            {
              "SettingsSchemaVersion": 3,
              "SpeechRate": 50,
              "SpeechVolume": -10,
              "ReaderSpeechRate": -50,
              "IntentModerationLevel": 99,
              "AiToxicityThreshold": 8,
              "SelectedSourceConnectorId": "",
              "CustomBlockedTerms": [" keep ", "", "KEEP"]
            }
            """);

        AppSettings loaded = new SettingsStore(directory.SettingsPath).Load();

        Assert.Equal(5, loaded.SpeechRate);
        Assert.Equal(0, loaded.SpeechVolume);
        Assert.Equal(-5, loaded.ReaderSpeechRate);
        Assert.Equal(4, loaded.IntentModerationLevel);
        Assert.Equal(0.95, loaded.AiToxicityThreshold);
        Assert.Equal("tikfinity", loaded.SelectedSourceConnectorId);
        Assert.Equal(["keep"], loaded.CustomBlockedTerms);
    }

    [Fact]
    public void Save_ReplacementKeepsPreviousValidPrimaryAsBackup()
    {
        using var directory = new TemporarySettingsDirectory();
        var store = new SettingsStore(directory.SettingsPath);
        var settings = new AppSettings { SpeechVolume = 20 };
        Assert.True(store.TrySave(settings, out string? firstError), firstError);
        string firstJson = File.ReadAllText(directory.SettingsPath);

        settings.SpeechVolume = 90;
        Assert.True(store.TrySave(settings, out string? secondError), secondError);

        using JsonDocument primary =
            JsonDocument.Parse(File.ReadAllText(directory.SettingsPath));
        using JsonDocument backup =
            JsonDocument.Parse(File.ReadAllText(store.BackupFilePath));
        Assert.Equal(90, primary.RootElement.GetProperty("SpeechVolume").GetInt32());
        Assert.Equal(20, backup.RootElement.GetProperty("SpeechVolume").GetInt32());
        Assert.Equal(firstJson, File.ReadAllText(store.BackupFilePath));
    }

    [Fact]
    public void Load_FutureSchemaDoesNotApplyUnsupportedValues()
    {
        using var directory = new TemporarySettingsDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(
            directory.SettingsPath,
            $$"""
            {
              "SettingsSchemaVersion": {{AppSettings.CurrentSettingsSchemaVersion + 1}},
              "Theme": 2,
              "SpokenGuidance": 2
            }
            """);

        AppSettings loaded = new SettingsStore(directory.SettingsPath).Load();

        Assert.Equal(ThemePreference.Unset, loaded.Theme);
        Assert.Equal(SpokenGuidanceMode.Unset, loaded.SpokenGuidance);
        Assert.Equal(OnboardingStage.Accessibility, loaded.OnboardingStage);
    }

    private sealed class TemporarySettingsDirectory : IDisposable
    {
        public TemporarySettingsDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SafeSpeak.Core.Tests",
                Guid.NewGuid().ToString("N"));
            SettingsPath = System.IO.Path.Combine(Path, "custom-settings.json");
        }

        public string Path { get; }

        public string SettingsPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
