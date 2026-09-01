using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class AccessibilityPreferencesConfirmationTests
{
    [Theory]
    [InlineData(SpokenGuidanceMode.Disabled, ThemePreference.Light)]
    [InlineData(SpokenGuidanceMode.Disabled, ThemePreference.Dark)]
    [InlineData(SpokenGuidanceMode.Disabled, ThemePreference.HighContrast)]
    [InlineData(SpokenGuidanceMode.Enabled, ThemePreference.Light)]
    [InlineData(SpokenGuidanceMode.Enabled, ThemePreference.Dark)]
    [InlineData(SpokenGuidanceMode.Enabled, ThemePreference.HighContrast)]
    public void MatchingCombinationAcrossTwoLaunchesConfirmsPreferences(
        SpokenGuidanceMode guidance,
        ThemePreference theme)
    {
        var settings = new AppSettings();

        AccessibilityPreferencesSelectionResult first =
            AccessibilityPreferencesConfirmation.Select(settings, guidance, theme);

        Assert.Equal(AccessibilityPreferencesSelectionResult.RestartRequired, first);
        Assert.Equal(guidance, settings.PendingSpokenGuidance);
        Assert.Equal(theme, settings.PendingTheme);
        Assert.False(settings.HasConfirmedAccessibilityPreferences);
        Assert.True(settings.IsAwaitingAccessibilityConfirmation);
        Assert.Equal(OnboardingStage.Accessibility, settings.OnboardingStage);
        Assert.Equal(guidance, settings.EffectiveSpokenGuidance);
        Assert.Equal(theme, settings.EffectiveTheme);

        AccessibilityPreferencesSelectionResult second =
            AccessibilityPreferencesConfirmation.Select(settings, guidance, theme);

        Assert.Equal(AccessibilityPreferencesSelectionResult.Confirmed, second);
        Assert.Equal(guidance, settings.SpokenGuidance);
        Assert.Equal(theme, settings.Theme);
        Assert.Equal(SpokenGuidanceMode.Unset, settings.PendingSpokenGuidance);
        Assert.Equal(ThemePreference.Unset, settings.PendingTheme);
        Assert.True(settings.HasConfirmedAccessibilityPreferences);
        Assert.False(settings.IsAwaitingAccessibilityConfirmation);
        Assert.Equal(OnboardingStage.Platform, settings.OnboardingStage);
    }

    [Theory]
    [InlineData(SpokenGuidanceMode.Disabled, ThemePreference.Light)]
    [InlineData(SpokenGuidanceMode.Enabled, ThemePreference.Dark)]
    public void DifferentSecondCombinationReplacesTheWholePendingCombination(
        SpokenGuidanceMode replacementGuidance,
        ThemePreference replacementTheme)
    {
        var settings = new AppSettings();
        AccessibilityPreferencesConfirmation.Select(
            settings,
            SpokenGuidanceMode.Enabled,
            ThemePreference.Light);

        AccessibilityPreferencesSelectionResult result =
            AccessibilityPreferencesConfirmation.Select(
                settings,
                replacementGuidance,
                replacementTheme);

        Assert.Equal(
            AccessibilityPreferencesSelectionResult.ChangedRestartRequired,
            result);
        Assert.Equal(replacementGuidance, settings.PendingSpokenGuidance);
        Assert.Equal(replacementTheme, settings.PendingTheme);
        Assert.Equal(SpokenGuidanceMode.Unset, settings.SpokenGuidance);
        Assert.Equal(ThemePreference.Unset, settings.Theme);
        Assert.False(settings.HasConfirmedAccessibilityPreferences);
        Assert.Equal(OnboardingStage.Accessibility, settings.OnboardingStage);
    }

    [Fact]
    public void PartialMigratedPendingChoiceStartsANewTwoLaunchConfirmation()
    {
        var settings = new AppSettings
        {
            PendingTheme = ThemePreference.HighContrast
        };

        AccessibilityPreferencesSelectionResult result =
            AccessibilityPreferencesConfirmation.Select(
                settings,
                SpokenGuidanceMode.Enabled,
                ThemePreference.HighContrast);

        Assert.Equal(AccessibilityPreferencesSelectionResult.RestartRequired, result);
        Assert.Equal(SpokenGuidanceMode.Enabled, settings.PendingSpokenGuidance);
        Assert.Equal(ThemePreference.HighContrast, settings.PendingTheme);
        Assert.True(settings.IsAwaitingAccessibilityConfirmation);
    }

    [Fact]
    public void SettingsChangeAppliesImmediatelyAndClearsPendingSelection()
    {
        var settings = new AppSettings
        {
            OnboardingStage = OnboardingStage.Complete,
            SpokenGuidance = SpokenGuidanceMode.Disabled,
            Theme = ThemePreference.Light,
            PendingSpokenGuidance = SpokenGuidanceMode.Disabled,
            PendingTheme = ThemePreference.HighContrast
        };

        AccessibilityPreferencesSelectionResult result =
            AccessibilityPreferencesConfirmation.Select(
                settings,
                SpokenGuidanceMode.Enabled,
                ThemePreference.Dark,
                applyImmediately: true);

        Assert.Equal(
            AccessibilityPreferencesSelectionResult.AppliedFromSettings,
            result);
        Assert.True(settings.HasConfirmedAccessibilityPreferences);
        Assert.True(settings.IsSpokenGuidanceEnabled);
        Assert.Equal(ThemePreference.Dark, settings.EffectiveTheme);
        Assert.Equal(SpokenGuidanceMode.Unset, settings.PendingSpokenGuidance);
        Assert.Equal(ThemePreference.Unset, settings.PendingTheme);
        Assert.Equal(OnboardingStage.Complete, settings.OnboardingStage);
        Assert.True(settings.HasCompletedOnboarding);
    }

    [Fact]
    public void EffectivePropertiesPreserveAPartialMigratedThemeWithoutConfirmingIt()
    {
        var settings = new AppSettings
        {
            PendingTheme = ThemePreference.HighContrast
        };

        Assert.False(settings.HasConfirmedAccessibilityPreferences);
        Assert.False(settings.IsAwaitingAccessibilityConfirmation);
        Assert.Equal(SpokenGuidanceMode.Unset, settings.EffectiveSpokenGuidance);
        Assert.Equal(ThemePreference.HighContrast, settings.EffectiveTheme);
        Assert.False(settings.IsSpokenGuidanceEnabled);
    }

    [Fact]
    public void ResetOnboardingClearsCurrentAndPendingPreferences()
    {
        var settings = new AppSettings
        {
            OnboardingStage = OnboardingStage.Complete,
            SpokenGuidance = SpokenGuidanceMode.Enabled,
            Theme = ThemePreference.Dark,
            PendingSpokenGuidance = SpokenGuidanceMode.Disabled,
            PendingTheme = ThemePreference.Light
        };

        settings.ResetOnboarding();

        Assert.Equal(SpokenGuidanceMode.Unset, settings.SpokenGuidance);
        Assert.Equal(ThemePreference.Unset, settings.Theme);
        Assert.Equal(SpokenGuidanceMode.Unset, settings.PendingSpokenGuidance);
        Assert.Equal(ThemePreference.Unset, settings.PendingTheme);
        Assert.Equal(OnboardingStage.Accessibility, settings.OnboardingStage);
        Assert.False(settings.HasCompletedOnboarding);
        Assert.False(settings.HasConfirmedAccessibilityPreferences);
        Assert.False(settings.IsAwaitingAccessibilityConfirmation);
    }

    [Fact]
    public void DisplayNamesUseLockedThemeNames()
    {
        Assert.Equal(
            "Light",
            AccessibilityPreferencesConfirmation.GetDisplayName(ThemePreference.Light));
        Assert.Equal(
            "Dark",
            AccessibilityPreferencesConfirmation.GetDisplayName(ThemePreference.Dark));
        Assert.Equal(
            "High Contrast",
            AccessibilityPreferencesConfirmation.GetDisplayName(
                ThemePreference.HighContrast));
    }

    [Fact]
    public void UnsetSelectionsAreRejected()
    {
        var settings = new AppSettings();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AccessibilityPreferencesConfirmation.Select(
                settings,
                SpokenGuidanceMode.Unset,
                ThemePreference.Light));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AccessibilityPreferencesConfirmation.Select(
                settings,
                SpokenGuidanceMode.Enabled,
                ThemePreference.Unset));
    }
}
