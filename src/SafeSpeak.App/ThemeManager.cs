using System.Windows;
using SafeSpeak.Core.Models;

namespace SafeSpeak.App;

/// <summary>
/// Applies the user's SafeSpeak theme while allowing Windows High Contrast to
/// temporarily override it with the user's Windows system colors.
/// </summary>
public static class ThemeManager
{
    private static readonly string[] PaletteResourceKeys =
    [
        "SafeSpeakWindowBrush",
        "SafeSpeakSurfaceBrush",
        "SafeSpeakSurfaceAltBrush",
        "SafeSpeakTextBrush",
        "SafeSpeakMutedTextBrush",
        "SafeSpeakBorderBrush",
        "SafeSpeakAccentBrush",
        "SafeSpeakAccentSoftBrush",
        "SafeSpeakAccentTextBrush",
        "SafeSpeakDangerBrush",
        "SafeSpeakDangerTextBrush",
        "SafeSpeakSuccessBrush",
        "SafeSpeakSuccessTextBrush"
    ];

    /// <summary>The normalized application theme requested by the user.</summary>
    public static ThemePreference RequestedTheme { get; private set; } = ThemePreference.Light;

    /// <summary>The theme currently represented by application resources.</summary>
    public static ThemePreference EffectiveTheme =>
        SystemParameters.HighContrast ? ThemePreference.HighContrast : RequestedTheme;

    /// <summary>Whether Windows is temporarily supplying SafeSpeak's colors.</summary>
    public static bool IsWindowsHighContrastOverrideActive => SystemParameters.HighContrast;

    /// <summary>Applies Light, Dark, or High Contrast. Unset safely falls back to Light.</summary>
    public static void Apply(ThemePreference theme)
    {
        RequestedTheme = Normalize(theme);
        ApplyEffectivePalette();
    }

    /// <summary>
    /// Re-evaluates the effective palette after a Windows system-parameter change.
    /// Call this when <see cref="SystemParameters.HighContrast"/> changes so turning
    /// Windows High Contrast off restores <see cref="RequestedTheme"/>.
    /// </summary>
    public static void RefreshForSystemSettings() => ApplyEffectivePalette();

    // Compatibility for callers being migrated from the former two-theme setting.
    public static void Apply(bool highContrast) =>
        Apply(highContrast ? ThemePreference.HighContrast : ThemePreference.Light);

    private static void ApplyEffectivePalette()
    {
        Application? application = Application.Current;
        if (application is null) return;

        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.Invoke(ApplyEffectivePalette);
            return;
        }

        ResourceDictionary palette = SystemParameters.HighContrast
            ? CreateWindowsHighContrastPalette()
            : LoadApplicationPalette(RequestedTheme);

        foreach (string key in PaletteResourceKeys)
        {
            application.Resources[key] = palette[key];
        }
    }

    private static ThemePreference Normalize(ThemePreference theme) => theme switch
    {
        ThemePreference.Dark => ThemePreference.Dark,
        ThemePreference.HighContrast => ThemePreference.HighContrast,
        _ => ThemePreference.Light
    };

    private static ResourceDictionary LoadApplicationPalette(ThemePreference theme)
    {
        string fileName = theme switch
        {
            ThemePreference.Dark => "Dark.xaml",
            ThemePreference.HighContrast => "HighContrast.xaml",
            _ => "Light.xaml"
        };

        return new ResourceDictionary
        {
            Source = new Uri($"Themes/{fileName}", UriKind.Relative)
        };
    }

    private static ResourceDictionary CreateWindowsHighContrastPalette() => new()
    {
        ["SafeSpeakWindowBrush"] = SystemColors.WindowBrush,
        ["SafeSpeakSurfaceBrush"] = SystemColors.WindowBrush,
        ["SafeSpeakSurfaceAltBrush"] = SystemColors.ControlBrush,
        ["SafeSpeakTextBrush"] = SystemColors.WindowTextBrush,
        ["SafeSpeakMutedTextBrush"] = SystemColors.WindowTextBrush,
        ["SafeSpeakBorderBrush"] = SystemColors.ActiveBorderBrush,
        ["SafeSpeakAccentBrush"] = SystemColors.HighlightBrush,
        ["SafeSpeakAccentSoftBrush"] = SystemColors.ControlBrush,
        ["SafeSpeakAccentTextBrush"] = SystemColors.HighlightTextBrush,
        ["SafeSpeakDangerBrush"] = SystemColors.HighlightBrush,
        ["SafeSpeakDangerTextBrush"] = SystemColors.HighlightTextBrush,
        ["SafeSpeakSuccessBrush"] = SystemColors.HighlightBrush,
        ["SafeSpeakSuccessTextBrush"] = SystemColors.HighlightTextBrush
    };
}
