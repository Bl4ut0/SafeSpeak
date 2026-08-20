using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class ReaderSetupConfirmationTests
{
    [Theory]
    [InlineData(true, ReaderSetupResult.ConfirmedEnabled)]
    [InlineData(false, ReaderSetupResult.ConfirmedDisabled)]
    public void TwoMatchingAnswersConfirmChoice(bool answer, ReaderSetupResult expected)
    {
        var confirmation = new ReaderSetupConfirmation();

        Assert.Equal(ReaderSetupResult.AwaitingMatchingAnswer, confirmation.Submit(answer));
        Assert.Equal(2, confirmation.AnswerNumber);
        Assert.Equal(expected, confirmation.Submit(answer));
        Assert.Equal(1, confirmation.AnswerNumber);
    }

    [Fact]
    public void MismatchRestartsAtFirstAnswer()
    {
        var confirmation = new ReaderSetupConfirmation();

        confirmation.Submit(enableReader: true);
        Assert.Equal(ReaderSetupResult.MismatchRestarted, confirmation.Submit(enableReader: false));
        Assert.Equal(1, confirmation.AnswerNumber);

        Assert.Equal(ReaderSetupResult.AwaitingMatchingAnswer, confirmation.Submit(enableReader: false));
        Assert.Equal(ReaderSetupResult.ConfirmedDisabled, confirmation.Submit(enableReader: false));
    }

    [Fact]
    public void APreviousMismatchCannotCountTowardConfirmation()
    {
        var confirmation = new ReaderSetupConfirmation();

        confirmation.Submit(enableReader: true);
        confirmation.Submit(enableReader: false);

        Assert.Equal(ReaderSetupResult.AwaitingMatchingAnswer, confirmation.Submit(enableReader: true));
        Assert.Equal(2, confirmation.AnswerNumber);
    }

    [Theory]
    [InlineData(AccessibilityProfile.Unset, true, false, false)]
    [InlineData(AccessibilityProfile.FullVoiceGuided, true, true, true)]
    [InlineData(AccessibilityProfile.HighContrastVisual, true, true, false)]
    [InlineData(AccessibilityProfile.StandardVisual, true, true, false)]
    [InlineData(AccessibilityProfile.FullVoiceGuided, false, false, true)]
    public void SettingsDeriveConfirmedAndReaderStates(
        AccessibilityProfile profile,
        bool setupCompleted,
        bool expectedConfirmed,
        bool expectedReaderEnabled)
    {
        var settings = new AppSettings
        {
            AccessibilityProfile = profile,
            HasCompletedAccessibilitySetup = setupCompleted
        };

        Assert.Equal(expectedConfirmed, settings.HasConfirmedReaderPreference);
        Assert.Equal(expectedReaderEnabled, settings.IsIntegratedReaderEnabled);
    }
}
