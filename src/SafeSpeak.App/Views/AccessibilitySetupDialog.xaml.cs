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
        if (e.Handled)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            (Keyboard.Modifiers & ~ModifierKeys.Control) == ModifierKeys.None)
        {
            int? step = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 1,
                Key.D2 or Key.NumPad2 => 2,
                Key.D3 or Key.NumPad3 => 3,
                Key.D4 or Key.NumPad4 => 4,
                Key.D5 or Key.NumPad5 => 5,
                Key.D6 or Key.NumPad6 => 6,
                Key.D7 or Key.NumPad7 => 7,
                Key.D8 or Key.NumPad8 => 8,
                _ => null
            };

            if (step.HasValue)
            {
                _viewModel.JumpToStepCommand.Execute(step.Value);
                e.Handled = true;
                return;
            }
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (_viewModel.CurrentPage == AccessibilitySetupPage.Reader)
        {
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

            return;
        }

        if (_viewModel.CurrentPage == AccessibilitySetupPage.Review)
        {
            if (e.Key == Key.Y &&
                _viewModel.IsPrimaryButtonVisible &&
                _viewModel.IsInteractionEnabled &&
                _viewModel.ContinueCommand.CanExecute(null))
            {
                _viewModel.ContinueCommand.Execute(null);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.N &&
                     _viewModel.IsInteractionEnabled &&
                     _viewModel.RestartSetupCommand.CanExecute(null))
            {
                _viewModel.RestartSetupCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Y &&
            Keyboard.FocusedElement is not TextBox &&
            _viewModel.IsPrimaryButtonVisible &&
            _viewModel.IsInteractionEnabled &&
            _viewModel.ContinueCommand.CanExecute(null))
        {
            _viewModel.ContinueCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ThemeList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ListBox selector || selector.Items.Count == 0)
        {
            return;
        }

        int nextIndex = e.Key switch
        {
            Key.Left or Key.Up => Math.Max(0, selector.SelectedIndex - 1),
            Key.Right or Key.Down => Math.Min(selector.Items.Count - 1, selector.SelectedIndex + 1),
            Key.Home => 0,
            Key.End => selector.Items.Count - 1,
            _ => -1
        };
        if (nextIndex < 0)
        {
            return;
        }

        selector.SelectedIndex = nextIndex;
        selector.ScrollIntoView(selector.SelectedItem);
        e.Handled = true;
    }

    private void ThemeList_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not ListBox selector ||
            e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(selector, source) is not ListBoxItem item)
        {
            return;
        }

        object selected = selector.ItemContainerGenerator.ItemFromContainer(item);
        if (selected == DependencyProperty.UnsetValue)
        {
            return;
        }

        selector.SelectedItem = selected;
        item.Focus();
        e.Handled = true;
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
                    AccessibilitySetupPage.Voice => VoiceSelectorComboBox,
                    AccessibilitySetupPage.Filtering => AiClassificationCheckBox,
                    AccessibilitySetupPage.Keybinds => KeybindsList,
                    AccessibilitySetupPage.Navigation => NavigationShortcutsList,
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
