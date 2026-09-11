using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SafeSpeak.App.Accessibility;
using SafeSpeak.App.ViewModels;
using SafeSpeak.Core.Accessibility;

namespace SafeSpeak.App.Views;

/// <summary>
/// Interaction logic for AccessibilitySetupDialog.xaml.
/// </summary>
public partial class AccessibilitySetupDialog : Window
{
    private readonly AccessibilitySetupViewModel _viewModel;
    private readonly IntegratedFocusNarrator _focusNarrator;

    private string? _modalFirstCapturedShortcut;
    private ModifierKeys _modalShortcutModifiers;
    private string? _modalShortcutKeyName;
    private bool _modalShortcutTriggerReleased;

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

        if (_viewModel.IsAnyModalOpen)
        {
            if (e.Key == Key.Escape)
            {
                if (_viewModel.IsEditingKeybind)
                {
                    _viewModel.CloseKeybindEditorCommand.Execute(null);
                }
                else
                {
                    _viewModel.CloseConnectorModalCommand.Execute(null);
                }
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Enter or Key.Return && _viewModel.IsConfiguringTikTokDirect)
            {
                _viewModel.SubmitTikTokUsernameCommand.Execute(null);
                e.Handled = true;
                return;
            }
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
                if (_viewModel.IsConfiguringTikTokDirect)
                {
                    TikTokUsernameTextBox.Focus();
                    Keyboard.Focus(TikTokUsernameTextBox);
                    return;
                }

                if (_viewModel.IsPlaceholderModalOpen)
                {
                    PlaceholderCloseButton.Focus();
                    Keyboard.Focus(PlaceholderCloseButton);
                    return;
                }

                if (_viewModel.IsEditingKeybind)
                {
                    _modalFirstCapturedShortcut = null;
                    ResetModalShortcutCaptureKeys();
                    ModalShortcutCaptureButton.Focus();
                    Keyboard.Focus(ModalShortcutCaptureButton);
                    return;
                }

                _focusNarrator.SuppressNextFocusAnnouncement();

                Control target = _viewModel.CurrentPage switch
                {
                    AccessibilitySetupPage.Reader => ReaderYesButton,
                    AccessibilitySetupPage.Theme => ThemeList,
                    AccessibilitySetupPage.Platform => TikFinityButton,
                    AccessibilitySetupPage.Voice => VoiceSelectorComboBox,
                    AccessibilitySetupPage.Filtering => MiniLmCardButton,
                    AccessibilitySetupPage.Keybinds => KeybindButton1,
                    AccessibilitySetupPage.Navigation => PrimaryButton,
                    AccessibilitySetupPage.Review => ReviewList,
                    _ => PrimaryButton
                };
                target.Focus();
                Keyboard.Focus(target);
            },
            DispatcherPriority.Input);
    }

    private void ModalBackdrop_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsAnyModalOpen)
        {
            if (_viewModel.IsEditingKeybind)
            {
                _viewModel.CloseKeybindEditorCommand.Execute(null);
            }
            else
            {
                _viewModel.CloseConnectorModalCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private void ModalShortcutCaptureButton_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys keyModifier = GetShortcutModifier(key);
        if (keyModifier != ModifierKeys.None)
        {
            _modalShortcutModifiers |= keyModifier;
            e.Handled = true;
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (key == Key.Tab &&
            _modalShortcutModifiers == ModifierKeys.None &&
            modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return;
        }

        if (key == Key.Escape &&
            _modalShortcutModifiers == ModifierKeys.None &&
            modifiers == ModifierKeys.None)
        {
            _viewModel.CloseKeybindEditorCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!TryGetShortcutKeyName(key, out string keyName))
        {
            _viewModel.KeybindCaptureStatus =
                $"{key} cannot be used as a global shortcut trigger. Try another combination.";
            _viewModel.Announcer.AnnounceFocus(_viewModel.KeybindCaptureStatus);
            e.Handled = true;
            return;
        }

        if (_modalShortcutKeyName is not null &&
            !string.Equals(_modalShortcutKeyName, keyName, StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.KeybindCaptureStatus =
                "Only one trigger key can be used. Release every held key, then try the complete shortcut again.";
            _viewModel.Announcer.AnnounceFocus(_viewModel.KeybindCaptureStatus);
            e.Handled = true;
            return;
        }

        _modalShortcutModifiers |= modifiers;
        _modalShortcutKeyName = keyName;
        _modalShortcutTriggerReleased = false;
        e.Handled = true;
    }

    private void ModalShortcutCaptureButton_PreviewKeyUp(
        object sender,
        KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys releasedModifier = GetShortcutModifier(key);
        if (releasedModifier != ModifierKeys.None)
        {
            _modalShortcutModifiers |= releasedModifier;
        }
        else if (_modalShortcutKeyName is not null &&
                 TryGetShortcutKeyName(key, out string releasedKeyName) &&
                 string.Equals(_modalShortcutKeyName, releasedKeyName, StringComparison.OrdinalIgnoreCase))
        {
            _modalShortcutTriggerReleased = true;
        }

        ModifierKeys remainingModifiers = releasedModifier == ModifierKeys.None
            ? Keyboard.Modifiers
            : Keyboard.Modifiers & ~releasedModifier;
        bool chordIsComplete = remainingModifiers == ModifierKeys.None &&
            ((_modalShortcutKeyName is not null && _modalShortcutTriggerReleased) ||
             (_modalShortcutKeyName is null && _modalShortcutModifiers != ModifierKeys.None));
        if (chordIsComplete)
        {
            string candidate = FormatShortcutGesture(
                _modalShortcutModifiers,
                _modalShortcutKeyName);
            ResetModalShortcutCaptureKeys();
            ProcessModalShortcutCapture(candidate);
        }

        e.Handled = true;
    }

    private void ModalShortcutCaptureButton_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e) => ResetModalShortcutCaptureKeys();

    private void ProcessModalShortcutCapture(string candidate)
    {
        if (_viewModel.EditingKeybind is not { } item)
        {
            return;
        }

        if (!GlobalShortcutGesture.TryParse(
                candidate,
                out GlobalShortcutGesture parsed,
                out string error))
        {
            _viewModel.KeybindCaptureStatus = $"That shortcut cannot be used. {error}";
            _viewModel.Announcer.AnnounceFocus(_viewModel.KeybindCaptureStatus);
            return;
        }

        string normalized = parsed.DisplayText;
        string spoken = normalized.Replace("+", " plus ", StringComparison.Ordinal);

        if (_modalFirstCapturedShortcut is null)
        {
            _modalFirstCapturedShortcut = normalized;
            _viewModel.KeybindCapturePrompt = $"First entry: {normalized}. Repeat it to confirm.";
            _viewModel.KeybindCaptureStatus =
                $"Captured {spoken}. Now press the same complete shortcut again to confirm it.";
            _viewModel.Announcer.AnnounceFocus(_viewModel.KeybindCaptureStatus);
            return;
        }

        if (!string.Equals(_modalFirstCapturedShortcut, normalized, StringComparison.OrdinalIgnoreCase))
        {
            string firstSpoken = _modalFirstCapturedShortcut.Replace("+", " plus ", StringComparison.Ordinal);
            _viewModel.KeybindCaptureStatus =
                $"The confirmation did not match. First entry was {firstSpoken}; second entry was {spoken}. Press {firstSpoken} again, or choose Cancel.";
            _viewModel.Announcer.AnnounceFocus(_viewModel.KeybindCaptureStatus);
            return;
        }

        _viewModel.KeybindCapturePrompt = $"Verified: {normalized}";
        _viewModel.SaveKeybind(item, normalized, enabled: true);
        _viewModel.StatusText = $"Verified and saved shortcut {normalized} for {item.DisplayName}.";
        _viewModel.Announcer.Announce($"Verified and saved {spoken} for {item.DisplayName}.", interrupt: true);
        _viewModel.CloseKeybindEditorCommand.Execute(null);
    }

    private void ResetModalShortcutCaptureKeys()
    {
        _modalShortcutModifiers = ModifierKeys.None;
        _modalShortcutKeyName = null;
        _modalShortcutTriggerReleased = false;
    }

    private static ModifierKeys GetShortcutModifier(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
        Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
        Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
        Key.LWin or Key.RWin => ModifierKeys.Windows,
        _ => ModifierKeys.None
    };

    private static string FormatShortcutGesture(
        ModifierKeys modifiers,
        string? keyName)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Control");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Windows");
        if (!string.IsNullOrEmpty(keyName)) parts.Add(keyName);
        return string.Join('+', parts);
    }

    private static bool TryGetShortcutKeyName(Key key, out string keyName)
    {
        int keyValue = (int)key;
        if (keyValue >= (int)Key.A && keyValue <= (int)Key.Z)
        {
            keyName = key.ToString();
            return true;
        }

        if (keyValue >= (int)Key.D0 && keyValue <= (int)Key.D9)
        {
            keyName = (keyValue - (int)Key.D0).ToString();
            return true;
        }

        if (keyValue >= (int)Key.NumPad0 && keyValue <= (int)Key.NumPad9)
        {
            keyName = $"Numpad{keyValue - (int)Key.NumPad0}";
            return true;
        }

        if (keyValue >= (int)Key.F1 && keyValue <= (int)Key.F24)
        {
            keyName = key.ToString();
            return true;
        }

        keyName = key switch
        {
            Key.Space => "Space",
            Key.Enter or Key.Return => "Enter",
            Key.Escape => "Escape",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Left => "Left",
            Key.Up => "Up",
            Key.Right => "Right",
            Key.Down => "Down",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Pause => "Pause",
            Key.NumLock => "NumLock",
            Key.Scroll => "ScrollLock",
            _ => string.Empty
        };
        return keyName.Length > 0;
    }

    private void AccessibilitySetupDialog_Closed(object? sender, EventArgs e)
    {
        _viewModel.FocusRequested -= ViewModel_FocusRequested;
        PreviewKeyDown -= AccessibilitySetupDialog_PreviewKeyDown;
        _focusNarrator.Dispose();
        _viewModel.Dispose();
    }
}
