using System.Windows;
using System.Windows.Interop;
using SafeSpeak.App.ViewModels;
using SafeSpeak.Core.Accessibility;

namespace SafeSpeak.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly GlobalHotkeyService _hotkeyService = new();
    private HwndSource? _hwndSource;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
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
            _hotkeyService.RegisterHotkeys(handle);
        }
        catch { }

        if (DataContext is MainViewModel vm)
        {
            vm.AnnounceState("SafeSpeak initialized. Press Ctrl Alt S anytime for private status.");
        }
    }

    private void HotkeyService_HotkeyTriggered(object? sender, HotkeyTriggeredEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        switch (e.Action)
        {
            case HotkeyAction.AnnounceStatus:
                vm.AnnounceStatusPrivately();
                break;
            case HotkeyAction.EmergencyPanic:
                vm.EmergencyPanic();
                break;
            case HotkeyAction.ToggleArm:
                vm.ToggleArm();
                break;
            case HotkeyAction.SkipCurrent:
                vm.SkipMessage();
                break;
        }
    }

    private nint HwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        _hotkeyService.ProcessWindowMessage(msg, wParam);
        return nint.Zero;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeyService.UnregisterHotkeys();
        _hwndSource?.RemoveHook(HwndHook);

        if (DataContext is MainViewModel vm)
        {
            _ = vm.DisposeAsync();
        }
    }
}