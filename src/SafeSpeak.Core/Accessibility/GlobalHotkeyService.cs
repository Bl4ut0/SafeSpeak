using System.Runtime.InteropServices;

namespace SafeSpeak.Core.Accessibility;

public enum HotkeyAction
{
    AnnounceStatus,
    EmergencyPanic,
    ToggleArm,
    SkipCurrent
}

public sealed class HotkeyTriggeredEventArgs : EventArgs
{
    public HotkeyAction Action { get; }

    public HotkeyTriggeredEventArgs(HotkeyAction action)
    {
        Action = action;
    }
}

/// <summary>
/// Registers system-wide global hotkeys for hands-free streamer accessibility.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;

    private const uint VK_S = 0x53;
    private const uint VK_P = 0x50;
    private const uint VK_A = 0x41;
    private const uint VK_K = 0x4B;

    private const int HOTKEY_ID_STATUS = 9001;
    private const int HOTKEY_ID_PANIC = 9002;
    private const int HOTKEY_ID_ARM = 9003;
    private const int HOTKEY_ID_SKIP = 9004;

    private nint _hWnd = nint.Zero;
    private bool _isRegistered = false;

    public event EventHandler<HotkeyTriggeredEventArgs>? HotkeyTriggered;

    public void RegisterHotkeys(nint windowHandle)
    {
        if (_isRegistered || windowHandle == nint.Zero) return;

        _hWnd = windowHandle;
        uint modifiers = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;

        // Ctrl + Alt + S: Status Announce
        RegisterHotKey(_hWnd, HOTKEY_ID_STATUS, modifiers, VK_S);

        // Ctrl + Alt + P: Emergency Panic Stop
        RegisterHotKey(_hWnd, HOTKEY_ID_PANIC, modifiers, VK_P);

        // Ctrl + Alt + A: Toggle Arm/Disarm
        RegisterHotKey(_hWnd, HOTKEY_ID_ARM, modifiers, VK_A);

        // Ctrl + Alt + K: Skip Message
        RegisterHotKey(_hWnd, HOTKEY_ID_SKIP, modifiers, VK_K);

        _isRegistered = true;
    }

    public void ProcessWindowMessage(int msg, nint wParam)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg != WM_HOTKEY) return;

        int id = wParam.ToInt32();
        HotkeyAction? action = id switch
        {
            HOTKEY_ID_STATUS => HotkeyAction.AnnounceStatus,
            HOTKEY_ID_PANIC => HotkeyAction.EmergencyPanic,
            HOTKEY_ID_ARM => HotkeyAction.ToggleArm,
            HOTKEY_ID_SKIP => HotkeyAction.SkipCurrent,
            _ => null
        };

        if (action.HasValue)
        {
            HotkeyTriggered?.Invoke(this, new HotkeyTriggeredEventArgs(action.Value));
        }
    }

    public void UnregisterHotkeys()
    {
        if (!_isRegistered || _hWnd == nint.Zero) return;

        UnregisterHotKey(_hWnd, HOTKEY_ID_STATUS);
        UnregisterHotKey(_hWnd, HOTKEY_ID_PANIC);
        UnregisterHotKey(_hWnd, HOTKEY_ID_ARM);
        UnregisterHotKey(_hWnd, HOTKEY_ID_SKIP);

        _isRegistered = false;
        _hWnd = nint.Zero;
    }

    public void Dispose()
    {
        UnregisterHotkeys();
    }
}
