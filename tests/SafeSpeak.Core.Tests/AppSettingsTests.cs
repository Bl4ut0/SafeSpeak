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
            SpeakUsernames = true,
            AiClassificationEnabled = true,
            AiToxicityThreshold = 0.8,
            CustomBlockedTerms = ["blocked phrase"]
        };

        ModerationConfig config = settings.CreateModerationConfig();
        config.CustomBlockedTerms.Add("second phrase");

        Assert.Equal(AudienceMode.SubscribersOnly, config.AudienceMode);
        Assert.Equal(ModerationStrictness.Maximum, config.Strictness);
        Assert.False(config.EnglishOnly);
        Assert.False(config.RejectMixedScripts);
        Assert.False(config.StripUrls);
        Assert.True(config.SpeakUsernames);
        Assert.True(config.AiClassificationEnabled);
        Assert.Equal(0.8, config.AiToxicityThreshold);
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
            AiToxicityThreshold = 2.0,
            CustomBlockedTerms = ["custom"]
        };

        settings.CaptureModerationConfig(config);
        config.CustomBlockedTerms.Clear();

        Assert.Equal(AudienceMode.ModeratorsOnly, settings.AudienceMode);
        Assert.Equal(ModerationStrictness.Standard, settings.Strictness);
        Assert.False(settings.EnglishOnly);
        Assert.Equal(0.95, settings.AiToxicityThreshold);
        Assert.Equal(["custom"], settings.CustomBlockedTerms);
    }
}
