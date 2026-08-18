using System.Windows;
using SafeSpeak.Infrastructure.Settings;

namespace SafeSpeak.App;

public partial class AccessibilitySetupWindow : Window
{
    public AccessibilitySetupWindow(AccessibilityMode initialMode)
    {
        InitializeComponent();
        FullyBlindOption.IsChecked = initialMode == AccessibilityMode.FullyBlind;
        PartiallySightedOption.IsChecked = initialMode == AccessibilityMode.PartiallySighted;
        StandardOption.IsChecked = initialMode == AccessibilityMode.Standard;
        Loaded += (_, _) => FullyBlindOption.Focus();
    }

    public AccessibilityMode SelectedMode => PartiallySightedOption.IsChecked == true
        ? AccessibilityMode.PartiallySighted
        : StandardOption.IsChecked == true
            ? AccessibilityMode.Standard
            : AccessibilityMode.FullyBlind;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
