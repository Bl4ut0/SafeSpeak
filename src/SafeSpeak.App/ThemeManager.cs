using System.Windows;
using System.Windows.Media;

namespace SafeSpeak.App;

/// <summary>Applies SafeSpeak's standard or optional extra-high-contrast palette.</summary>
public static class ThemeManager
{
    public static void Apply(bool highContrast)
    {
        if (Application.Current is null) return;
        if (SystemParameters.HighContrast)
        {
            SetBrush("SafeSpeakWindowBrush", SystemColors.WindowBrush);
            SetBrush("SafeSpeakSurfaceBrush", SystemColors.WindowBrush);
            SetBrush("SafeSpeakSurfaceAltBrush", SystemColors.ControlBrush);
            SetBrush("SafeSpeakTextBrush", SystemColors.WindowTextBrush);
            SetBrush("SafeSpeakMutedTextBrush", SystemColors.WindowTextBrush);
            SetBrush("SafeSpeakBorderBrush", SystemColors.ActiveBorderBrush);
            SetBrush("SafeSpeakAccentBrush", SystemColors.HighlightBrush);
            SetBrush("SafeSpeakAccentTextBrush", SystemColors.HighlightTextBrush);
            SetBrush("SafeSpeakDangerBrush", SystemColors.HighlightBrush);
            return;
        }
        Set("SafeSpeakWindowBrush", highContrast ? "#FF000000" : "#FFF7F8FC");
        Set("SafeSpeakSurfaceBrush", highContrast ? "#FF000000" : "#FFFFFFFF");
        Set("SafeSpeakSurfaceAltBrush", highContrast ? "#FF111111" : "#FFF0F3FA");
        Set("SafeSpeakTextBrush", highContrast ? "#FFFFFFFF" : "#FF152033");
        Set("SafeSpeakMutedTextBrush", highContrast ? "#FFFFFFFF" : "#FF506078");
        Set("SafeSpeakBorderBrush", highContrast ? "#FFFFFFFF" : "#FFC7D0E0");
        Set("SafeSpeakAccentBrush", highContrast ? "#FFFFFF00" : "#FF2459D3");
        Set("SafeSpeakAccentTextBrush", highContrast ? "#FF000000" : "#FFFFFFFF");
        Set("SafeSpeakDangerBrush", highContrast ? "#FFFFFF00" : "#FFB42318");
    }

    private static void Set(string key, string color) =>
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private static void SetBrush(string key, Brush brush) => Application.Current.Resources[key] = brush;
}
