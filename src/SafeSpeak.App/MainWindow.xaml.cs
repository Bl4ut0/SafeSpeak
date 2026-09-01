using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly GlobalHotkeyService _hotkeyService = new();
    private HwndSource? _hwndSource;
    private IntegratedFocusNarrator? _focusNarrator;
    private bool _shutdownCleanupStarted;
    private bool _shutdownCleanupComplete;

    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is MainViewModel vm)
        {
            _focusNarrator = new IntegratedFocusNarrator(this, vm.Announcer);
        }
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            HearStatusButton.Focus();
            Keyboard.Focus(HearStatusButton);
        }, DispatcherPriority.Input);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Tab)
        {
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
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

    private UIElement GetSelectedPageEntryControl() => MainNavigation.SelectedIndex switch
    {
        0 => ArmToggle,
        1 => ModerationSlider,
        2 => VoiceCombo,
        3 => ThemeSelector,
        _ => ArmToggle
    };

    private static void FocusElement(UIElement element)
    {
        element.Focus();
        Keyboard.Focus(element);
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
            var handle = helper.EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(HwndHook);

            _hotkeyService.HotkeyTriggered += HotkeyService_HotkeyTriggered;
            HotkeyRegistrationResult registration = _hotkeyService.RegisterHotkeys(handle);
            if (!registration.AllRegistered && DataContext is MainViewModel registrationViewModel)
            {
                string shortcuts = string.Join(
                    ", ",
                    registration.Unavailable.Select(GetShortcutName));
                registrationViewModel.AnnounceState(
                    $"Warning: these global shortcuts are already in use and are unavailable: {shortcuts}. The on-screen controls still work.",
                    interrupt: true);
            }
        }
        catch { }

        if (DataContext is MainViewModel startupViewModel)
        {
            startupViewModel.AnnounceState("SafeSpeak initialized. Press Control Alt S anytime to hear status.");
        }
    }

    private static string GetShortcutName(HotkeyAction action) => action switch
    {
        HotkeyAction.AnnounceStatus => "Control Alt S",
        HotkeyAction.EmergencyStop => "Control Alt P",
        HotkeyAction.ToggleArm => "Control Alt A",
        HotkeyAction.StopCurrentSpeech => "Control Alt K",
        _ => action.ToString()
    };

    private void HotkeyService_HotkeyTriggered(object? sender, HotkeyTriggeredEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        switch (e.Action)
        {
            case HotkeyAction.AnnounceStatus:
                vm.AnnounceStatusPrivately();
                break;
            case HotkeyAction.EmergencyStop:
                vm.EmergencyStop();
                break;
            case HotkeyAction.ToggleArm:
                vm.ToggleArm();
                break;
            case HotkeyAction.StopCurrentSpeech:
                vm.StopCurrentSpeech();
                break;
        }
    }

    private nint HwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        _hotkeyService.ProcessWindowMessage(msg, wParam);
        return nint.Zero;
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownCleanupComplete)
        {
            return;
        }

        e.Cancel = true;
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
                Task cleanupTask = Task.Run(async () => await vm.DisposeAsync());
                Task completedTask = await Task.WhenAny(cleanupTask, Task.Delay(ShutdownTimeout));
                if (completedTask == cleanupTask)
                {
                    await cleanupTask;
                }
                else
                {
                    _ = ObserveCleanupFailureAsync(cleanupTask);
                }
            }
        }
        catch
        {
            // Shutdown must remain non-blocking and accessible. Cleanup is
            // best-effort; process exit releases any remaining native model,
            // speech, or audio resources.
        }
        finally
        {
            _shutdownCleanupComplete = true;
            Application.Current.Shutdown();
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
