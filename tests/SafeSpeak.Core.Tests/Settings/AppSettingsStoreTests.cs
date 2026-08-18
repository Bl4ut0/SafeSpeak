using SafeSpeak.Infrastructure.Settings;

namespace SafeSpeak.Core.Tests.Settings;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public async Task SavesAndLoadsAccessibilityPreferences()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SafeSpeak-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");

        try
        {
            var store = new AppSettingsStore(path);
            var expected = new AppSettings
            {
                FirstRunComplete = true,
                AccessibilityMode = AccessibilityMode.PartiallySighted,
                SpokenGuidanceEnabled = true,
                EnglishOnly = false,
                AutomaticPlayback = true,
            };

            await store.SaveAsync(expected);
            AppSettings actual = await store.LoadAsync();

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CorruptSettingsFallBackToSafeDefaults()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "not-json");
            var store = new AppSettingsStore(path);

            AppSettings settings = await store.LoadAsync();

            Assert.False(settings.FirstRunComplete);
            Assert.Equal(AccessibilityMode.FullyBlind, settings.AccessibilityMode);
            Assert.False(settings.SpokenGuidanceEnabled);
            Assert.True(settings.EnglishOnly);
            Assert.False(settings.AutomaticPlayback);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LegacyFullyBlindSettingsEnableSpokenGuidance()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                path,
                "{\"firstRunComplete\":true,\"accessibilityMode\":0,\"englishOnly\":true}");
            var store = new AppSettingsStore(path);

            AppSettings settings = await store.LoadAsync();

            Assert.True(settings.SpokenGuidanceEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
