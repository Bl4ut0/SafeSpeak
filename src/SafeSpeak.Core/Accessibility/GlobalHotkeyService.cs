using System.Runtime.InteropServices;

namespace SafeSpeak.Core.Accessibility;

public sealed class HotkeyTriggeredEventArgs : EventArgs
{
    public HotkeyAction Action { get; }

    public HotkeyTriggeredEventArgs(HotkeyAction action)
    {
        Action = action;
    }
}

public sealed record HotkeyRegistrationIssue(
    HotkeyAction Action,
    string Gesture,
    string Reason);

public sealed record HotkeyRegistrationResult(
    IReadOnlyList<GlobalShortcutBinding> Registered,
    IReadOnlyList<HotkeyRegistrationIssue> Unavailable)
{
    public bool AllRegistered => Unavailable.Count == 0;
}

/// <summary>
/// Registers customizable system-wide shortcuts while SafeSpeak is running.
/// Standard combinations use RegisterHotKey. Modifier-only combinations, such
/// as the default Control key used to silence built-in guidance, are observed
/// by a non-blocking low-level keyboard hook and are never consumed.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WH_KEYBOARD_LL = 13;
    private const int HotkeyIdBase = 9000;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private const uint VK_BACK = 0x08;
    private const uint VK_TAB = 0x09;
    private const uint VK_RETURN = 0x0D;
    private const uint VK_SHIFT = 0x10;
    private const uint VK_CONTROL = 0x11;
    private const uint VK_MENU = 0x12;
    private const uint VK_PAUSE = 0x13;
    private const uint VK_CAPITAL = 0x14;
    private const uint VK_ESCAPE = 0x1B;
    private const uint VK_SPACE = 0x20;
    private const uint VK_PRIOR = 0x21;
    private const uint VK_NEXT = 0x22;
    private const uint VK_END = 0x23;
    private const uint VK_HOME = 0x24;
    private const uint VK_LEFT = 0x25;
    private const uint VK_UP = 0x26;
    private const uint VK_RIGHT = 0x27;
    private const uint VK_DOWN = 0x28;
    private const uint VK_INSERT = 0x2D;
    private const uint VK_DELETE = 0x2E;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_RWIN = 0x5C;
    private const uint VK_NUMLOCK = 0x90;
    private const uint VK_SCROLL = 0x91;
    private const uint VK_LSHIFT = 0xA0;
    private const uint VK_RSHIFT = 0xA1;
    private const uint VK_LCONTROL = 0xA2;
    private const uint VK_RCONTROL = 0xA3;
    private const uint VK_LMENU = 0xA4;
    private const uint VK_RMENU = 0xA5;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc callback,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hookHandle,
        int code,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    private delegate nint LowLevelKeyboardProc(
        int code,
        nint wParam,
        nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LowLevelKeyboardInput
    {
        public readonly uint VirtualKey;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInfo;
    }

    private readonly Dictionary<int, HotkeyAction> _actionsById = new();
    private readonly Dictionary<uint, List<(GlobalShortcutGesture Gesture, HotkeyAction Action)>>
        _modifierOnlyBindings = new();
    private readonly HashSet<uint> _pressedModifierKeys = new();
    private readonly LowLevelKeyboardProc _keyboardProc;
    private nint _windowHandle;
    private nint _keyboardHook;

    public GlobalHotkeyService()
    {
        _keyboardProc = KeyboardHookCallback;
    }

    public event EventHandler<HotkeyTriggeredEventArgs>? HotkeyTriggered;

    public HotkeyRegistrationResult RegisterHotkeys(
        nint windowHandle,
        IEnumerable<GlobalShortcutBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        UnregisterHotkeys();
        _windowHandle = windowHandle;

        GlobalShortcutBinding[] enabled = bindings
            .Where(binding => binding.IsEnabled)
            .Select(binding => binding.Clone())
            .ToArray();
        var registered = new List<GlobalShortcutBinding>(enabled.Length);
        var unavailable = new List<HotkeyRegistrationIssue>();
        var usedGestures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingModifierOnly = new List<(GlobalShortcutBinding Binding, GlobalShortcutGesture Gesture, uint Key)>();

        foreach (GlobalShortcutBinding binding in enabled)
        {
            if (!Enum.IsDefined(binding.Action))
            {
                unavailable.Add(new(binding.Action, binding.Gesture, "The shortcut action is not supported."));
                continue;
            }

            if (!GlobalShortcutGesture.TryParse(
                    binding.Gesture,
                    out GlobalShortcutGesture gesture,
                    out string parseError))
            {
                unavailable.Add(new(binding.Action, binding.Gesture, parseError));
                continue;
            }

            binding.Gesture = gesture.DisplayText;
            if (!usedGestures.Add(binding.Gesture))
            {
                unavailable.Add(new(binding.Action, binding.Gesture, "Another SafeSpeak action uses this shortcut."));
                continue;
            }

            if (!TryGetVirtualKey(gesture.Key, out uint virtualKey))
            {
                unavailable.Add(new(binding.Action, binding.Gesture, "Windows does not support this key."));
                continue;
            }

            if (gesture.IsModifierOnly)
            {
                pendingModifierOnly.Add((binding, gesture, virtualKey));
                continue;
            }

            if (_windowHandle == nint.Zero)
            {
                unavailable.Add(new(binding.Action, binding.Gesture, "The SafeSpeak window is not ready."));
                continue;
            }

            int id = HotkeyIdBase + (int)binding.Action;
            uint nativeModifiers = ToNativeModifiers(gesture.Modifiers) | MOD_NOREPEAT;
            if (RegisterHotKey(_windowHandle, id, nativeModifiers, virtualKey))
            {
                _actionsById[id] = binding.Action;
                registered.Add(binding);
            }
            else
            {
                unavailable.Add(new(binding.Action, binding.Gesture, "This shortcut is already in use by Windows or another application."));
            }
        }

        if (pendingModifierOnly.Count > 0)
        {
            _keyboardHook = SetWindowsHookEx(
                WH_KEYBOARD_LL,
                _keyboardProc,
                GetModuleHandle(null),
                0);
            if (_keyboardHook == nint.Zero)
            {
                foreach ((GlobalShortcutBinding binding, _, _) in pendingModifierOnly)
                {
                    unavailable.Add(new(binding.Action, binding.Gesture, "Windows could not start the modifier-key listener."));
                }
            }
            else
            {
                foreach ((GlobalShortcutBinding binding, GlobalShortcutGesture gesture, uint key) in pendingModifierOnly)
                {
                    if (!_modifierOnlyBindings.TryGetValue(
                            key,
                            out List<(GlobalShortcutGesture Gesture, HotkeyAction Action)>? keyBindings))
                    {
                        keyBindings = [];
                        _modifierOnlyBindings[key] = keyBindings;
                    }
                    keyBindings.Add((gesture, binding.Action));
                    registered.Add(binding);
                }
            }
        }

        return new HotkeyRegistrationResult(registered, unavailable);
    }

    public void ProcessWindowMessage(int message, nint wParam)
    {
        if (message != WM_HOTKEY) return;

        int id = wParam.ToInt32();
        if (_actionsById.TryGetValue(id, out HotkeyAction action))
        {
            HotkeyTriggered?.Invoke(this, new HotkeyTriggeredEventArgs(action));
        }
    }

    public void UnregisterHotkeys()
    {
        if (_windowHandle != nint.Zero)
        {
            foreach (int id in _actionsById.Keys)
            {
                _ = UnregisterHotKey(_windowHandle, id);
            }
        }

        if (_keyboardHook != nint.Zero)
        {
            _ = UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = nint.Zero;
        }

        _actionsById.Clear();
        _modifierOnlyBindings.Clear();
        _pressedModifierKeys.Clear();
        _windowHandle = nint.Zero;
    }

    private nint KeyboardHookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            int message = unchecked((int)wParam.ToInt64());
            var input = Marshal.PtrToStructure<LowLevelKeyboardInput>(lParam);
            uint normalizedKey = NormalizeModifierVirtualKey(input.VirtualKey);

            if (message is WM_KEYUP or WM_SYSKEYUP)
            {
                _pressedModifierKeys.Remove(normalizedKey);
            }
            else if (
                (message is WM_KEYDOWN or WM_SYSKEYDOWN) &&
                _modifierOnlyBindings.TryGetValue(normalizedKey, out var keyBindings) &&
                _pressedModifierKeys.Add(normalizedKey))
            {
                GlobalShortcutModifiers modifiers = CurrentModifiersExcept(normalizedKey);
                foreach ((GlobalShortcutGesture gesture, HotkeyAction action) in keyBindings)
                {
                    if (gesture.Modifiers == modifiers)
                    {
                        HotkeyTriggered?.Invoke(
                            this,
                            new HotkeyTriggeredEventArgs(action));
                        break;
                    }
                }
            }
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private static GlobalShortcutModifiers CurrentModifiersExcept(uint key)
    {
        GlobalShortcutModifiers modifiers = GlobalShortcutModifiers.None;
        if (IsPressed(VK_LCONTROL) || IsPressed(VK_RCONTROL)) modifiers |= GlobalShortcutModifiers.Control;
        if (IsPressed(VK_LMENU) || IsPressed(VK_RMENU)) modifiers |= GlobalShortcutModifiers.Alt;
        if (IsPressed(VK_LSHIFT) || IsPressed(VK_RSHIFT)) modifiers |= GlobalShortcutModifiers.Shift;
        if (IsPressed(VK_LWIN) || IsPressed(VK_RWIN)) modifiers |= GlobalShortcutModifiers.Windows;

        return key switch
        {
            VK_CONTROL => modifiers & ~GlobalShortcutModifiers.Control,
            VK_MENU => modifiers & ~GlobalShortcutModifiers.Alt,
            VK_SHIFT => modifiers & ~GlobalShortcutModifiers.Shift,
            VK_LWIN => modifiers & ~GlobalShortcutModifiers.Windows,
            _ => modifiers
        };
    }

    private static bool IsPressed(uint virtualKey) =>
        (GetAsyncKeyState(unchecked((int)virtualKey)) & 0x8000) != 0;

    private static uint NormalizeModifierVirtualKey(uint virtualKey) => virtualKey switch
    {
        VK_LCONTROL or VK_RCONTROL => VK_CONTROL,
        VK_LMENU or VK_RMENU => VK_MENU,
        VK_LSHIFT or VK_RSHIFT => VK_SHIFT,
        VK_RWIN => VK_LWIN,
        _ => virtualKey
    };

    private static uint ToNativeModifiers(GlobalShortcutModifiers modifiers)
    {
        uint native = 0;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Alt)) native |= MOD_ALT;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Control)) native |= MOD_CONTROL;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Shift)) native |= MOD_SHIFT;
        if (modifiers.HasFlag(GlobalShortcutModifiers.Windows)) native |= MOD_WIN;
        return native;
    }

    private static bool TryGetVirtualKey(string key, out uint virtualKey)
    {
        virtualKey = key switch
        {
            "Backspace" => VK_BACK,
            "Tab" => VK_TAB,
            "Enter" => VK_RETURN,
            "Shift" => VK_SHIFT,
            "Control" => VK_CONTROL,
            "Alt" => VK_MENU,
            "Pause" => VK_PAUSE,
            "CapsLock" => VK_CAPITAL,
            "Escape" => VK_ESCAPE,
            "Space" => VK_SPACE,
            "PageUp" => VK_PRIOR,
            "PageDown" => VK_NEXT,
            "End" => VK_END,
            "Home" => VK_HOME,
            "Left" => VK_LEFT,
            "Up" => VK_UP,
            "Right" => VK_RIGHT,
            "Down" => VK_DOWN,
            "Insert" => VK_INSERT,
            "Delete" => VK_DELETE,
            "Windows" => VK_LWIN,
            "NumLock" => VK_NUMLOCK,
            "ScrollLock" => VK_SCROLL,
            _ => 0
        };

        if (virtualKey != 0) return true;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            virtualKey = char.ToUpperInvariant(key[0]);
            return true;
        }

        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out int functionKey))
        {
            virtualKey = 0x70u + unchecked((uint)(functionKey - 1));
            return functionKey is >= 1 and <= 24;
        }

        if (key.StartsWith("Numpad", StringComparison.Ordinal) &&
            int.TryParse(key.AsSpan(6), out int numpadKey))
        {
            virtualKey = 0x60u + unchecked((uint)numpadKey);
            return numpadKey is >= 0 and <= 9;
        }

        return false;
    }

    public void Dispose()
    {
        UnregisterHotkeys();
    }
}
