using System.Runtime.InteropServices;

namespace SafeSpeak.Core.Accessibility;

public enum HotkeyAction
{
    AnnounceStatus,
    EmergencyStop,
    ToggleArm,
    StopCurrentSpeech
}

public sealed class HotkeyTriggeredEventArgs : EventArgs
{
    public HotkeyAction Action { get; }

    public HotkeyTriggeredEventArgs(HotkeyAction action)
    {
        Action = action;
    }
}

public sealed record HotkeyRegistrationResult(
    IReadOnlyList<HotkeyAction> Registered,
    IReadOnlyList<HotkeyAction> Unavailable)
{
    public bool AllRegistered => Unavailable.Count == 0;
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

    // Keep the numeric IDs stable so upgrades retain the Windows hotkey contract.
    private const int HOTKEY_ID_STATUS = 9001;
    private const int HOTKEY_ID_EMERGENCY_STOP = 9002;
    private const int HOTKEY_ID_ARM = 9003;
    private const int HOTKEY_ID_STOP_CURRENT_SPEECH = 9004;

    private nint _hWnd = nint.Zero;
    private readonly HashSet<int> _registeredIds = new();

    public event EventHandler<HotkeyTriggeredEventArgs>? HotkeyTriggered;

    public HotkeyRegistrationResult RegisterHotkeys(nint windowHandle)
    {
        if (_registeredIds.Count > 0 || windowHandle == nint.Zero)
        {
            return new HotkeyRegistrationResult(Array.Empty<HotkeyAction>(), Enum.GetValues<HotkeyAction>());
        }

        _hWnd = windowHandle;
        uint modifiers = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;
        var registered = new List<HotkeyAction>();
        var unavailable = new List<HotkeyAction>();

        TryRegister(HOTKEY_ID_STATUS, VK_S, HotkeyAction.AnnounceStatus);
        TryRegister(HOTKEY_ID_EMERGENCY_STOP, VK_P, HotkeyAction.EmergencyStop);
        TryRegister(HOTKEY_ID_ARM, VK_A, HotkeyAction.ToggleArm);
        TryRegister(HOTKEY_ID_STOP_CURRENT_SPEECH, VK_K, HotkeyAction.StopCurrentSpeech);

        return new HotkeyRegistrationResult(registered, unavailable);

        void TryRegister(int id, uint key, HotkeyAction action)
        {
            if (RegisterHotKey(_hWnd, id, modifiers, key))
            {
                _registeredIds.Add(id);
                registered.Add(action);
            }
            else
            {
                unavailable.Add(action);
            }
        }
    }

    public void ProcessWindowMessage(int msg, nint wParam)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg != WM_HOTKEY) return;

        int id = wParam.ToInt32();
        HotkeyAction? action = id switch
        {
            HOTKEY_ID_STATUS => HotkeyAction.AnnounceStatus,
            HOTKEY_ID_EMERGENCY_STOP => HotkeyAction.EmergencyStop,
            HOTKEY_ID_ARM => HotkeyAction.ToggleArm,
            HOTKEY_ID_STOP_CURRENT_SPEECH => HotkeyAction.StopCurrentSpeech,
            _ => null
        };

        if (action.HasValue)
        {
            HotkeyTriggered?.Invoke(this, new HotkeyTriggeredEventArgs(action.Value));
        }
    }

    public void UnregisterHotkeys()
    {
        if (_hWnd == nint.Zero) return;

        foreach (int id in _registeredIds)
        {
            UnregisterHotKey(_hWnd, id);
        }
        _registeredIds.Clear();
        _hWnd = nint.Zero;
    }

    public void Dispose()
    {
        UnregisterHotkeys();
    }
}
