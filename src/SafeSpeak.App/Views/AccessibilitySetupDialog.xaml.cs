using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SafeSpeak.App.Accessibility;
using SafeSpeak.App.ViewModels;

namespace SafeSpeak.App.Views;

/// <summary>
/// Interaction logic for AccessibilitySetupDialog.xaml
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
        Closed += AccessibilitySetupDialog_Closed;
        Loaded += (_, _) => FocusYesButton();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.Y)
        {
            _viewModel.AnswerYesCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.N)
        {
            _viewModel.AnswerNoCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ViewModel_FocusRequested(object? sender, EventArgs e) => FocusYesButton();

    private void FocusYesButton()
    {
        Dispatcher.BeginInvoke(() =>
        {
            YesButton.Focus();
            Keyboard.Focus(YesButton);
        }, DispatcherPriority.Input);
    }

    private void AccessibilitySetupDialog_Closed(object? sender, EventArgs e)
    {
        _viewModel.FocusRequested -= ViewModel_FocusRequested;
        _focusNarrator.Dispose();
        _viewModel.Dispose();
    }
}
