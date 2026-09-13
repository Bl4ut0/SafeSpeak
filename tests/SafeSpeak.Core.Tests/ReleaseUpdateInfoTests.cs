using SafeSpeak.Core.Models;
using Xunit;

namespace SafeSpeak.Core.Tests;

public sealed class ReleaseUpdateInfoTests
{
    [Fact]
    public void CurrentGuideVersion_IsPositiveAndMatchesAppSettings()
    {
        Assert.True(ReleaseUpdateInfo.CurrentGuideVersion > 0);
        Assert.Equal(ReleaseUpdateInfo.CurrentGuideVersion, AppSettings.CurrentSetupGuideVersion);
    }

    [Fact]
    public void CurrentHighlights_ContainsMeaningfulBulletPoints()
    {
        Assert.NotEmpty(ReleaseUpdateInfo.CurrentHighlights);
        Assert.True(ReleaseUpdateInfo.CurrentHighlights.Count >= 2);
        foreach (string highlight in ReleaseUpdateInfo.CurrentHighlights)
        {
            Assert.False(string.IsNullOrWhiteSpace(highlight));
        }
    }

    [Fact]
    public void GetAnnouncementText_IncludesAllHighlightsAndKeyActions()
    {
        string announcement = ReleaseUpdateInfo.GetAnnouncementText();

        Assert.Contains("SafeSpeak settings and setup update", announcement);
        Assert.Contains("What's new in this version:", announcement);
        Assert.Contains("Press Y to review the setup guide", announcement);
        Assert.Contains("press N to keep current settings", announcement);
        Assert.Contains("press R to repeat this announcement", announcement);

        foreach (string highlight in ReleaseUpdateInfo.CurrentHighlights)
        {
            Assert.Contains(highlight.TrimEnd('.'), announcement);
        }
    }

    [Fact]
    public void GetHelpText_IncludesAllHighlightsAndKeyActions()
    {
        string helpText = ReleaseUpdateInfo.GetHelpText();

        Assert.Contains("SafeSpeak settings and setup options have updated", helpText);
        Assert.Contains("Press Y to review the setup guide", helpText);
        Assert.Contains("press N to keep current settings", helpText);
        Assert.Contains("press R to repeat the announcement", helpText);

        foreach (string highlight in ReleaseUpdateInfo.CurrentHighlights)
        {
            Assert.Contains(highlight.TrimEnd('.'), helpText);
        }
    }

    [Fact]
    public void GetFormattedBulletHighlights_PrefixesEveryItemWithBullet()
    {
        IReadOnlyList<string> bulletItems = ReleaseUpdateInfo.GetFormattedBulletHighlights();

        Assert.Equal(ReleaseUpdateInfo.CurrentHighlights.Count, bulletItems.Count);
        foreach (string item in bulletItems)
        {
            Assert.StartsWith("\u2022 ", item);
        }
    }

    [Fact]
    public void CustomHighlights_AreFormattedCorrectlyInAnnouncementAndHelpText()
    {
        string[] custom = ["Feature Alpha: Tested successfully.", "Feature Beta: Enabled by default."];

        string announcement = ReleaseUpdateInfo.GetAnnouncementText(custom);
        string helpText = ReleaseUpdateInfo.GetHelpText(custom);
        IReadOnlyList<string> bulletItems = ReleaseUpdateInfo.GetFormattedBulletHighlights(custom);

        Assert.Contains("Feature Alpha: Tested successfully.", announcement);
        Assert.Contains("Feature Beta: Enabled by default.", announcement);
        Assert.Contains("Feature Alpha: Tested successfully.", helpText);
        Assert.Contains("Feature Beta: Enabled by default.", helpText);

        Assert.Equal(2, bulletItems.Count);
        Assert.Equal("\u2022 Feature Alpha: Tested successfully.", bulletItems[0]);
        Assert.Equal("\u2022 Feature Beta: Enabled by default.", bulletItems[1]);
    }
}
