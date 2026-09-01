using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class OnboardingResumeSettingsTests
{
    [Fact]
    public void LegacySettings_DefaultToNoDetectionConsentOrResult()
    {
        using var directory = new TemporarySettingsDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(
            directory.SettingsPath,
            """
            {
              "SettingsSchemaVersion": 3,
              "SpokenGuidance": 2,
              "Theme": 2,
              "OnboardingStage": 3
            }
            """);

        AppSettings settings = AppSettings.Load(directory.SettingsPath);

        Assert.False(settings.LocalConnectorAutoDetectConsent);
        Assert.Equal(
            OnboardingConnectorDetectionStatus.NotChecked,
            settings.LocalConnectorDetectionStatus);
        Assert.Equal(
            "Local connector detection was not requested.",
            settings.LocalConnectorDetectionSummary);
    }

    [Fact]
    public void DetectionConsentStatusAndSafeSummary_RoundTrip()
    {
        using var directory = new TemporarySettingsDirectory();
        var settings = AppSettings.Load(directory.SettingsPath);
        settings.LocalConnectorAutoDetectConsent = true;
        settings.LocalConnectorDetectionStatus =
            OnboardingConnectorDetectionStatus.NotDetected;
        settings.LocalConnectorDetectionSummary =
            "TikFinity was not detected. You can still select it and connect later.";

        Assert.True(settings.TrySave(out string? error), error);
        AppSettings reloaded = AppSettings.Load(directory.SettingsPath);

        Assert.True(reloaded.LocalConnectorAutoDetectConsent);
        Assert.Equal(
            OnboardingConnectorDetectionStatus.NotDetected,
            reloaded.LocalConnectorDetectionStatus);
        Assert.Equal(
            settings.LocalConnectorDetectionSummary,
            reloaded.LocalConnectorDetectionSummary);
    }

    [Fact]
    public void DetectionSummary_IsReplacedWhenItContainsUntrustedProbeDetails()
    {
        using var directory = new TemporarySettingsDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(
            directory.SettingsPath,
            """
            {
              "SettingsSchemaVersion": 4,
              "LocalConnectorAutoDetectConsent": true,
              "LocalConnectorDetectionStatus": 1,
              "LocalConnectorDetectionSummary": "process secret.exe listened at 10.0.0.2:65535"
            }
            """);

        AppSettings settings = AppSettings.Load(directory.SettingsPath);

        Assert.Equal(
            "TikFinity appears to be available on this computer.",
            settings.LocalConnectorDetectionSummary);
        Assert.DoesNotContain("secret", settings.LocalConnectorDetectionSummary);
        Assert.DoesNotContain("65535", settings.LocalConnectorDetectionSummary);
    }

    [Fact]
    public void InvalidDetectionStatus_ResetsToNotCheckedWithBoundedSummary()
    {
        using var directory = new TemporarySettingsDirectory();
        Directory.CreateDirectory(directory.Path);
        string oversizedSummary = new('x', 4096);
        File.WriteAllText(
            directory.SettingsPath,
            $$"""
            {
              "SettingsSchemaVersion": 4,
              "LocalConnectorAutoDetectConsent": true,
              "LocalConnectorDetectionStatus": 999,
              "LocalConnectorDetectionSummary": "{{oversizedSummary}}"
            }
            """);

        AppSettings settings = AppSettings.Load(directory.SettingsPath);

        Assert.Equal(
            OnboardingConnectorDetectionStatus.NotChecked,
            settings.LocalConnectorDetectionStatus);
        Assert.Equal(
            "Local connector detection was not requested.",
            settings.LocalConnectorDetectionSummary);
        Assert.True(settings.LocalConnectorDetectionSummary.Length <= 512);
    }

    [Fact]
    public void RevokedConsent_ClearsPreviouslyPersistedDetectionResult()
    {
        using var directory = new TemporarySettingsDirectory();
        var settings = AppSettings.Load(directory.SettingsPath);
        settings.LocalConnectorAutoDetectConsent = false;
        settings.LocalConnectorDetectionStatus =
            OnboardingConnectorDetectionStatus.Detected;
        settings.LocalConnectorDetectionSummary =
            "TikFinity appears to be available on this computer.";

        Assert.True(settings.TrySave(out string? error), error);
        AppSettings reloaded = AppSettings.Load(directory.SettingsPath);

        Assert.False(reloaded.LocalConnectorAutoDetectConsent);
        Assert.Equal(
            OnboardingConnectorDetectionStatus.NotChecked,
            reloaded.LocalConnectorDetectionStatus);
        Assert.Equal(
            "Local connector detection was not requested.",
            reloaded.LocalConnectorDetectionSummary);
    }

    private sealed class TemporarySettingsDirectory : IDisposable
    {
        public TemporarySettingsDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SafeSpeak.OnboardingResume.Tests",
                Guid.NewGuid().ToString("N"));
            SettingsPath = System.IO.Path.Combine(Path, "settings.json");
        }

        public string Path { get; }
        public string SettingsPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
