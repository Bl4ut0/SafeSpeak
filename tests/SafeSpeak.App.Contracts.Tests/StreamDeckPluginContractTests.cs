using System.Text.Json;

namespace SafeSpeak.App.Contracts.Tests;

public sealed class StreamDeckPluginContractTests
{
    private static readonly string[] EssentialActions =
    [
        "Hear Status",
        "Arm / Disarm",
        "Emergency Stop",
        "Playback Mode",
        "Pause / Resume TTS",
        "Speak Next",
        "Stop Current",
        "Clear Queue"
    ];

    [Fact]
    public void Manifest_AdvertisesOnlyEssentialLiveActions_InAccessibleOrder()
    {
        using JsonDocument manifest = JsonDocument.Parse(Source("streamdeck", "manifest.json"));
        JsonElement actions = manifest.RootElement.GetProperty("Actions");

        string[] names = actions.EnumerateArray()
            .Select(action => action.GetProperty("Name").GetString()!)
            .ToArray();
        string[] identifiers = actions.EnumerateArray()
            .Select(action => action.GetProperty("UUID").GetString()!)
            .ToArray();

        Assert.Equal(EssentialActions, names);
        Assert.Equal(EssentialActions.Length, identifiers.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Plugin_UsesCanonicalLiveCommands_AndNoSettingsToggleCatalog()
    {
        string script = Source("streamdeck", "app.js");

        foreach (string command in new[]
        {
            "status",
            "toggle_arm",
            "emergency_stop",
            "toggle_autoplay",
            "toggle_pause",
            "speak_next",
            "stop_current",
            "clear_queue"
        })
        {
            Assert.Contains($"sendSafeSpeakCommand(\"{command}\")", script);
        }

        foreach (string obsoleteAction in new[]
        {
            ".english",
            ".usernames",
            ".aiclassifier",
            ".audience",
            ".strictness",
            ".connection",
            ".chat",
            ".gifts",
            ".follows",
            ".shares",
            ".subscriptions",
            ".joins",
            ".likes",
            ".broadcast",
            ".private",
            ".highcontrast"
        })
        {
            Assert.DoesNotContain($"com.safespeak.streamdeck{obsoleteAction}", script);
        }
    }

    [Fact]
    public void RemovedStreamDeckConfiguration_RemainsAvailableInAccessibleAppPages()
    {
        string window = Source("src", "SafeSpeak.App", "MainWindow.xaml");

        foreach (string binding in new[]
        {
            "SelectedTheme",
            "ModerationLevel",
            "EnglishOnly",
            "RejectMixedScripts",
            "SelectedAudienceMode",
            "AllowDonorsToSpeak",
            "AnnounceChatMessages",
            "AnnounceGifts",
            "AnnounceFollows",
            "AnnounceShares",
            "AnnounceSubscriptions",
            "AnnounceJoins",
            "AnnounceLikes",
            "BroadcastOutputEnabled"
        })
        {
            Assert.Contains($"Binding {binding}", window);
        }

        Assert.Contains("RequiredSafetyFeaturesStatus", window);
        Assert.Contains("Reconnect to live stream source button", window);
        Assert.DoesNotContain("Binding PrivateMonitorEnabled", window);
        Assert.DoesNotContain("Private monitor output", window);
    }

    [Fact]
    public void Settings_ExposePauseAllAndPerEventBypassChoices()
    {
        string window = Source("src", "SafeSpeak.App", "MainWindow.xaml");

        Assert.Contains("Binding PauseAllTtsWhilePaused", window);
        Assert.Contains("Binding AllowGiftAnnouncementsWhilePaused", window);
        Assert.Contains("Binding AllowFollowAnnouncementsWhilePaused", window);
        Assert.Contains("Binding AllowShareAnnouncementsWhilePaused", window);
        Assert.Contains("Binding AllowSubscriptionAnnouncementsWhilePaused", window);
        Assert.Contains("Emergency Stop always stops", window);
    }

    private static string Source(params string[] segments) =>
        File.ReadAllText(RepositoryFile(segments));

    private static string RepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SafeSpeak.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(segments).ToArray());
    }
}
