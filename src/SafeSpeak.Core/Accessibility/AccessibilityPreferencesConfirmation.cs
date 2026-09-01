using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Accessibility;

/// <summary>
/// Applies the independent spoken-guidance and visual-theme confirmation
/// policy. Startup choices must match as a combination across two launches;
/// choices made from Settings apply immediately. Persistence is owned by the
/// caller so save failures can be surfaced accessibly.
/// </summary>
public static class AccessibilityPreferencesConfirmation
{
    public static AccessibilityPreferencesSelectionResult Select(
        AppSettings settings,
        SpokenGuidanceMode spokenGuidance,
        ThemePreference theme,
        bool applyImmediately = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSelection(spokenGuidance, theme);

        if (applyImmediately)
        {
            ApplyConfirmedPreferences(settings, spokenGuidance, theme);
            return AccessibilityPreferencesSelectionResult.AppliedFromSettings;
        }

        bool hasCompletePendingSelection =
            settings.PendingSpokenGuidance != SpokenGuidanceMode.Unset &&
            settings.PendingTheme != ThemePreference.Unset;

        if (!hasCompletePendingSelection)
        {
            StorePendingSelection(settings, spokenGuidance, theme);
            return AccessibilityPreferencesSelectionResult.RestartRequired;
        }

        if (
            settings.PendingSpokenGuidance == spokenGuidance &&
            settings.PendingTheme == theme)
        {
            ApplyConfirmedPreferences(settings, spokenGuidance, theme);
            settings.OnboardingStage = OnboardingStage.Platform;
            return AccessibilityPreferencesSelectionResult.Confirmed;
        }

        StorePendingSelection(settings, spokenGuidance, theme);
        return AccessibilityPreferencesSelectionResult.ChangedRestartRequired;
    }

    public static string GetDisplayName(SpokenGuidanceMode spokenGuidance) =>
        spokenGuidance switch
        {
            SpokenGuidanceMode.Enabled => "Built-in spoken guidance on",
            SpokenGuidanceMode.Disabled => "Built-in spoken guidance off",
            _ => "Spoken guidance not selected"
        };

    public static string GetDisplayName(ThemePreference theme) => theme switch
    {
        ThemePreference.Light => "Light",
        ThemePreference.Dark => "Dark",
        ThemePreference.HighContrast => "High Contrast",
        _ => "Theme not selected"
    };

    private static void ValidateSelection(
        SpokenGuidanceMode spokenGuidance,
        ThemePreference theme)
    {
        if (
            spokenGuidance == SpokenGuidanceMode.Unset ||
            !Enum.IsDefined(spokenGuidance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(spokenGuidance),
                "A concrete spoken-guidance choice is required.");
        }

        if (theme == ThemePreference.Unset || !Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(
                nameof(theme),
                "A concrete theme choice is required.");
        }
    }

    private static void StorePendingSelection(
        AppSettings settings,
        SpokenGuidanceMode spokenGuidance,
        ThemePreference theme)
    {
        settings.OnboardingStage = OnboardingStage.Accessibility;
        settings.SpokenGuidance = SpokenGuidanceMode.Unset;
        settings.Theme = ThemePreference.Unset;
        settings.PendingSpokenGuidance = spokenGuidance;
        settings.PendingTheme = theme;
    }

    private static void ApplyConfirmedPreferences(
        AppSettings settings,
        SpokenGuidanceMode spokenGuidance,
        ThemePreference theme)
    {
        settings.SpokenGuidance = spokenGuidance;
        settings.Theme = theme;
        settings.PendingSpokenGuidance = SpokenGuidanceMode.Unset;
        settings.PendingTheme = ThemePreference.Unset;
    }
}

public enum AccessibilityPreferencesSelectionResult
{
    RestartRequired,
    ChangedRestartRequired,
    Confirmed,
    AppliedFromSettings
}
