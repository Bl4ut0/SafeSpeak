using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SafeSpeak.App.Accessibility;
using SafeSpeak.App.ViewModels;

namespace SafeSpeak.App.Views;

/// <summary>
/// Interaction logic for AccessibilitySetupDialog.xaml.
/// </summary>
public partial class AccessibilitySetupDialog : Window
{
    private readonly AccessibilitySetupViewModel _viewModel;
    private readonly IntegratedFocusNarrator _focusNarrator;

    public AccessibilitySetupDialog(AccessibilitySetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        _focusNarrator = new IntegratedFocusNarrator(this, _viewModel.Announcer);
        _viewModel.FocusRequested += ViewModel_FocusRequested;
        PreviewKeyDown += AccessibilitySetupDialog_PreviewKeyDown;
        Closed += AccessibilitySetupDialog_Closed;
        Loaded += (_, _) =>
        {
            FocusPrimaryControl();
            Dispatcher.BeginInvoke(
                _viewModel.AnnounceInitialPrompt,
                DispatcherPriority.ContextIdle);
        };
    }

    private void ViewModel_FocusRequested(object? sender, EventArgs e) =>
        FocusPrimaryControl();

    private void AccessibilitySetupDialog_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Handled ||
            _viewModel.CurrentPage != AccessibilitySetupPage.Reader ||
            Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.Y)
        {
            _viewModel.ChooseSpokenGuidanceYesCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.N)
        {
            _viewModel.ChooseSpokenGuidanceNoCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void FocusPrimaryControl()
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                Control target = _viewModel.CurrentPage switch
                {
                    AccessibilitySetupPage.Reader => ReaderYesButton,
                    AccessibilitySetupPage.Theme => ThemeList,
                    AccessibilitySetupPage.Platform => TikFinityCheckBox,
                    AccessibilitySetupPage.Review => ReviewList,
                    _ => PrimaryButton
                };
                target.Focus();
                Keyboard.Focus(target);
            },
            DispatcherPriority.Input);
    }

    private void AccessibilitySetupDialog_Closed(object? sender, EventArgs e)
    {
        _viewModel.FocusRequested -= ViewModel_FocusRequested;
        PreviewKeyDown -= AccessibilitySetupDialog_PreviewKeyDown;
        _focusNarrator.Dispose();
        _viewModel.Dispose();
    }
}
