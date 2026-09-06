using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SafeSpeak.App.Accessibility;
using SafeSpeak.App.ViewModels;
using SafeSpeak.Core.Accessibility;

namespace SafeSpeak.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly GlobalHotkeyService _hotkeyService = new();
    private nint _windowHandle;
    private HwndSource? _hwndSource;
    private IntegratedFocusNarrator? _focusNarrator;
    private bool _shutdownCleanupStarted;
    private ModifierKeys _shortcutCaptureModifiers;
    private bool _shortcutCapturedNonModifier;

    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is MainViewModel vm)
        {
            _focusNarrator = new IntegratedFocusNarrator(
                this,
                vm.Announcer,
                () => vm.NarrateDetailedHelp,
                () => vm.NarrateTypedCharacters);
            vm.GlobalShortcutsChanged += ViewModel_GlobalShortcutsChanged;
        }
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            HearStatusButton.Focus();
            Keyboard.Focus(HearStatusButton);
        }, DispatcherPriority.Input);
    }

    private void GlobalShortcutGestureInput_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Handled || sender is not TextBox input)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys keyModifier = GetShortcutModifier(key);
        if (keyModifier != ModifierKeys.None)
        {
            if (_shortcutCaptureModifiers == ModifierKeys.None)
            {
                _shortcutCapturedNonModifier = false;
            }

            _shortcutCaptureModifiers |= keyModifier;
            e.Handled = true;
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        bool isKeyboardNavigation = key == Key.Tab &&
            modifiers is ModifierKeys.None or ModifierKeys.Shift;
        if (isKeyboardNavigation)
        {
            return;
        }

        if (key is Key.Back or Key.Delete && modifiers == ModifierKeys.None)
        {
            SetCapturedGlobalShortcut(input, string.Empty);
            _shortcutCapturedNonModifier = true;
            e.Handled = true;
            return;
        }

        if (!TryGetShortcutKeyName(key, out string keyName))
        {
            if (DataContext is MainViewModel unsupportedViewModel)
            {
                unsupportedViewModel.Announcer.AnnounceFocus(
                    $"{key} cannot be used as a global shortcut key.");
            }

            e.Handled = true;
            return;
        }

        string gesture = FormatShortcutGesture(modifiers, keyName);
        SetCapturedGlobalShortcut(input, gesture);
        _shortcutCapturedNonModifier = true;
        e.Handled = true;
    }

    private void ThemeSelector_PreviewKeyDown(object sender, KeyEventArgs e)
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

    private void SettingsPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled ||
            e.Key != Key.Tab ||
            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0 ||
            !SettingsPanel.IsKeyboardFocusWithin)
        {
            return;
        }

        List<Control> orderedStops = EnumerateVisualDescendants(SettingsPanel)
            .OfType<Control>()
            .Where(control =>
                control.IsVisible &&
                control.IsEnabled &&
                control.Focusable &&
                control.IsTabStop &&
                KeyboardNavigation.GetTabIndex(control) < int.MaxValue)
            .OrderBy(KeyboardNavigation.GetTabIndex)
            .ToList();
        if (orderedStops.Count == 0)
        {
            return;
        }

        DependencyObject? focused = Keyboard.FocusedElement as DependencyObject;
        int currentIndex = orderedStops.FindIndex(control =>
            ReferenceEquals(control, focused) ||
            (focused is not null && control.IsAncestorOf(focused)));
        if (currentIndex < 0)
        {
            return;
        }

        bool reverse = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        int nextIndex = currentIndex + (reverse ? -1 : 1);
        if (nextIndex < 0)
        {
            // The window-level navigation handler returns Shift+Tab from the
            // page entry control to the Settings tab header.
            return;
        }

        if (nextIndex >= orderedStops.Count)
        {
            FocusElement(HearStatusButton);
        }
        else
        {
            Control target = orderedStops[nextIndex];
            target.BringIntoView();
            FocusElement(target);
        }

        e.Handled = true;
    }

    private static IEnumerable<DependencyObject> EnumerateVisualDescendants(
        DependencyObject root)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in EnumerateVisualDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private void GlobalShortcutGestureInput_PreviewKeyUp(
        object sender,
        KeyEventArgs e)
    {
        if (sender is not TextBox input)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys releasedModifier = GetShortcutModifier(key);
        if (releasedModifier == ModifierKeys.None)
        {
            return;
        }

        _shortcutCaptureModifiers |= releasedModifier;
        ModifierKeys remainingModifiers = Keyboard.Modifiers & ~releasedModifier;
        if (remainingModifiers == ModifierKeys.None)
        {
            if (!_shortcutCapturedNonModifier)
            {
                string gesture = FormatShortcutGesture(_shortcutCaptureModifiers, null);
                SetCapturedGlobalShortcut(input, gesture);
            }

            ResetGlobalShortcutCapture();
        }

        e.Handled = true;
    }

    private void GlobalShortcutGestureInput_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e) => ResetGlobalShortcutCapture();

    private void SetCapturedGlobalShortcut(TextBox input, string gesture)
    {
        if (DataContext is not MainViewModel viewModel ||
            viewModel.SelectedGlobalShortcut is not { } editor)
        {
            return;
        }

        editor.Gesture = gesture;
        editor.Status = string.IsNullOrEmpty(gesture)
            ? "Shortcut cleared; record another shortcut or turn this action off before applying."
            : $"Recorded {gesture}; choose Apply shortcut changes.";
        input.CaretIndex = gesture.Length;

        string spokenGesture = string.IsNullOrEmpty(gesture)
            ? "cleared"
            : gesture.Replace("+", " plus ", StringComparison.Ordinal);
        viewModel.Announcer.AnnounceFocus(
            $"Shortcut keys for {editor.DisplayName}: {spokenGesture}. " +
            "Choose Apply shortcut changes to activate it.");
    }

    private void ResetGlobalShortcutCapture()
    {
        _shortcutCaptureModifiers = ModifierKeys.None;
        _shortcutCapturedNonModifier = false;
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
            Key.Back => "Backspace",
            Key.Tab => "Tab",
            Key.Return => "Enter",
            Key.Pause => "Pause",
            Key.Capital => "CapsLock",
            Key.Escape => "Escape",
            Key.Space => "Space",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.End => "End",
            Key.Home => "Home",
            Key.Left => "Left",
            Key.Up => "Up",
            Key.Right => "Right",
            Key.Down => "Down",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.NumLock => "NumLock",
            Key.Scroll => "ScrollLock",
            _ => string.Empty
        };
        return keyName.Length > 0;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        // Do not steal combinations while the user is recording a custom
        // global shortcut. That editor owns its complete key sequence.
        if (ReferenceEquals(Keyboard.FocusedElement, GlobalShortcutGestureInput))
        {
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        bool controlOnly = (modifiers & ModifierKeys.Control) != 0 &&
            (modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) == 0;
        if (controlOnly && TryHandleDirectNavigationShortcut(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Tab)
        {
            return;
        }

        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
        {
            return;
        }

        bool reverse = (modifiers & ModifierKeys.Shift) != 0;
        IInputElement? focusedElement = Keyboard.FocusedElement;
        if (ReferenceEquals(focusedElement, MainNavigation) ||
            focusedElement is TabItem focusedTab && MainNavigation.Items.Contains(focusedTab))
        {
            FocusElement(reverse ? HearStatusButton : GetSelectedPageEntryControl());
            e.Handled = true;
            return;
        }

        UIElement pageEntryControl = GetSelectedPageEntryControl();
        if (reverse && pageEntryControl.IsKeyboardFocusWithin)
        {
            if (MainNavigation.SelectedItem is TabItem selectedTab)
            {
                FocusElement(selectedTab);
            }
            else
            {
                FocusElement(MainNavigation);
            }

            e.Handled = true;
        }
    }

    private bool TryHandleDirectNavigationShortcut(Key key)
    {
        int? tabIndex = key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            _ => null
        };
        if (tabIndex is not null)
        {
            SelectNavigationTab(tabIndex.Value);
            return true;
        }

        return false;
    }

    private void SelectNavigationTab(int index)
    {
        if (index < 0 || index >= MainNavigation.Items.Count)
        {
            return;
        }

        MainNavigation.SelectedIndex = index;
        Dispatcher.BeginInvoke(() =>
        {
            if (MainNavigation.SelectedItem is TabItem selectedTab)
            {
                FocusElement(selectedTab);
            }
        }, DispatcherPriority.Input);
    }

    private UIElement GetSelectedTab() =>
        MainNavigation.SelectedItem is TabItem selectedTab
            ? selectedTab
            : MainNavigation;

    private UIElement GetSelectedPageEntryControl() => MainNavigation.SelectedIndex switch
    {
        0 => ArmToggle,
        1 => DataContext is MainViewModel { AreSafetyGuideControlsAtTop: true }
            ? ReadModerationGuideButton
            : ModerationSlider,
        2 => VoiceCombo,
        3 => DataContext is MainViewModel { AreSettingsGuideControlsAtTop: true }
            ? SettingsGuideButton
            : ThemeSelector,
        _ => ArmToggle
    };

    private void GuidePlacementButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        bool isSafety = button.Name.Contains("Safety", StringComparison.Ordinal);
        bool movedToTop = isSafety
            ? viewModel.AreSafetyGuideControlsAtTop
            : viewModel.AreSettingsGuideControlsAtTop;
        UIElement focusTarget = (isSafety, movedToTop) switch
        {
            (true, true) => ReadModerationGuideButton,
            (true, false) => ModerationSlider,
            (false, true) => SettingsGuideButton,
            _ => ThemeSelector
        };
        string page = isSafety ? "Safety" : "Settings";
        string destination = movedToTop ? "top" : "end";
        string focusName = movedToTop
            ? "Play whole guide"
            : isSafety ? "Moderation strength" : "Theme selector";

        Dispatcher.BeginInvoke(() =>
        {
            FocusElement(focusTarget);
            viewModel.AnnounceState(
                $"{page} guide buttons moved to the {destination} of the page. Focus is now on {focusName}.",
                interrupt: true);
        }, DispatcherPriority.ContextIdle);
    }

    private static void FocusElement(UIElement element)
    {
        element.Focus();
        Keyboard.Focus(element);
    }

    private void PlaybackModeButton_Click(object sender, RoutedEventArgs e)
    {
        UIElement target = ReferenceEquals(sender, UseManualPlaybackButton)
            ? SpeakNextApprovedMessageButton
            : PauseOrResumeButton;

        Dispatcher.BeginInvoke(() =>
        {
            if (target.IsVisible && target.IsEnabled)
            {
                FocusElement(target);
            }
        }, DispatcherPriority.Input);
    }

    private void LiveFeedListView_GotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PauseLiveFeedReview();
        }
    }

    private void LiveFeedListView_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!LiveFeedListView.IsKeyboardFocusWithin &&
                DataContext is MainViewModel viewModel)
            {
                viewModel.ResumeLiveFeedReview();
            }
        }, DispatcherPriority.Input);
    }

    private void EmergencyStopButton_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded || e.NewValue is not false) return;

        // Emergency Stop and ordinary disarm both collapse the armed-only
        // controls. Return keyboard focus to the persistent re-arm action
        // instead of leaving it on a control that no longer exists visually.
        Dispatcher.BeginInvoke(() =>
        {
            ArmToggle.Focus();
            Keyboard.Focus(ArmToggle);
        }, DispatcherPriority.Input);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            var helper = new WindowInteropHelper(this);
            _windowHandle = helper.EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource?.AddHook(HwndHook);

            _hotkeyService.HotkeyTriggered += HotkeyService_HotkeyTriggered;
            if (DataContext is MainViewModel registrationViewModel)
            {
                RegisterCurrentGlobalShortcuts(registrationViewModel, announce: false);
            }
        }
        catch { }

        if (DataContext is MainViewModel startupViewModel)
        {
            GlobalShortcutBinding? statusShortcut = startupViewModel
                .GetGlobalShortcutBindings()
                .FirstOrDefault(binding =>
                    binding.Action == HotkeyAction.AnnounceStatus &&
                    binding.IsEnabled);
            string guidance = statusShortcut is null
                ? "SafeSpeak initialized. Global shortcuts can be configured in Settings."
                : $"SafeSpeak initialized. Press {statusShortcut.Gesture} anytime to hear status.";
            startupViewModel.AnnounceState(guidance);
        }
    }

    private void ViewModel_GlobalShortcutsChanged(object? sender, EventArgs e)
    {
        if (sender is MainViewModel viewModel)
        {
            RegisterCurrentGlobalShortcuts(viewModel, announce: true);
        }
    }

    private void RegisterCurrentGlobalShortcuts(
        MainViewModel viewModel,
        bool announce)
    {
        HotkeyRegistrationResult registration = _hotkeyService.RegisterHotkeys(
            _windowHandle,
            viewModel.GetGlobalShortcutBindings());
        viewModel.ReportGlobalShortcutRegistration(
            registration,
            announce || !registration.AllRegistered);
    }

    private void HotkeyService_HotkeyTriggered(object? sender, HotkeyTriggeredEventArgs e)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (DataContext is MainViewModel vm)
            {
                await vm.ExecuteGlobalShortcutAsync(e.Action);
            }
        });
    }

    private nint HwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        _hotkeyService.ProcessWindowMessage(msg, wParam);
        return nint.Zero;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownCleanupStarted)
        {
            return;
        }

        _shutdownCleanupStarted = true;
        try
        {
            TryShutdownStep(_hotkeyService.Dispose);
            TryShutdownStep(() => _hwndSource?.RemoveHook(HwndHook));
            TryShutdownStep(() => _focusNarrator?.Dispose());

            if (DataContext is MainViewModel vm)
            {
                vm.GlobalShortcutsChanged -= ViewModel_GlobalShortcutsChanged;
                // Backend cancellation, native TTS teardown, connector disposal,
                // and disk flushing must never hold the WPF window open. Settings
                // are already persisted as they change, so cleanup is best-effort.
                _ = ObserveCleanupFailureAsync(
                    Task.Run(async () => await vm.DisposeAsync().ConfigureAwait(false)));
            }
        }
        catch
        {
            // Shutdown must remain non-blocking and accessible. Cleanup is
            // best-effort; process exit releases any remaining native model,
            // speech, or audio resources.
        }
    }

    private static async Task ObserveCleanupFailureAsync(Task cleanupTask)
    {
        try { await cleanupTask.ConfigureAwait(false); }
        catch { }
    }

    private static void TryShutdownStep(Action operation)
    {
        try { operation(); }
        catch { }
    }
}
