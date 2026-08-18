using System.Windows;
using System.Windows.Input;
using SafeSpeak.App.Accessibility;
using SafeSpeak.Infrastructure.Settings;

namespace SafeSpeak.App;

public partial class AccessibilitySetupWindow : Window
{
    internal const string SpokenPrompt =
        "Welcome to SafeSpeak. Would you like SafeSpeak spoken guidance enabled? " +
        "Press Enter or Y for yes. Press N for no. Press P for partially sighted mode.";

    private readonly ISpokenGuidanceService _spokenGuidance;
    private readonly bool _announcePrompt;

    public AccessibilitySetupWindow(
        AccessibilityMode initialMode,
        ISpokenGuidanceService spokenGuidance,
        bool announcePrompt)
    {
        SelectedMode = initialMode;
        _spokenGuidance = spokenGuidance;
        _announcePrompt = announcePrompt;
        InitializeComponent();
        Loaded += Window_Loaded;
        Closed += (_, _) => _spokenGuidance.Stop();
    }

    public AccessibilityMode SelectedMode { get; private set; }

    public bool SpokenGuidanceEnabled => SelectedMode is not AccessibilityMode.Standard;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        YesButton.Focus();
        if (_announcePrompt)
        {
            _spokenGuidance.Speak(SpokenPrompt);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Y:
                Complete(AccessibilityMode.FullyBlind);
                e.Handled = true;
                break;
            case Key.N:
                Complete(AccessibilityMode.Standard);
                e.Handled = true;
                break;
            case Key.P:
                Complete(AccessibilityMode.PartiallySighted);
                e.Handled = true;
                break;
        }
    }

    private void YesButton_Click(object sender, RoutedEventArgs e) =>
        Complete(AccessibilityMode.FullyBlind);

    private void NoButton_Click(object sender, RoutedEventArgs e) =>
        Complete(AccessibilityMode.Standard);

    private void PartiallySightedButton_Click(object sender, RoutedEventArgs e) =>
        Complete(AccessibilityMode.PartiallySighted);

    private void Complete(AccessibilityMode mode)
    {
        SelectedMode = mode;
        DialogResult = true;
    }
}
