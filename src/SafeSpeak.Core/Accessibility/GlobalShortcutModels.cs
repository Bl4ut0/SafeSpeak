namespace SafeSpeak.Core.Accessibility;

public enum HotkeyAction
{
    // Values 1 through 4 preserve the original SafeSpeak hotkey identifiers.
    AnnounceStatus = 1,
    EmergencyStop = 2,
    ToggleArm = 3,
    StopCurrentSpeech = 4,
    StopBuiltInGuidance = 5,
    ToggleAutomaticPlayback = 6,
    TogglePause = 7,
    SpeakNext = 8,
    ClearQueue = 9,
    ToggleConnection = 10,
    ToggleSpokenGuidance = 11,
    ToggleHighContrast = 12,
    CycleAudience = 13,
    CycleModerationStrength = 14,
    ToggleEnglishOnly = 15,
    ToggleMixedScriptProtection = 16,
    ToggleDonorEligibility = 17,
    ToggleChatAnnouncements = 18,
    ToggleGiftAnnouncements = 19,
    ToggleFollowAnnouncements = 20,
    ToggleShareAnnouncements = 21,
    ToggleSubscriptionAnnouncements = 22,
    ToggleJoinAnnouncements = 23,
    ToggleLikeAnnouncements = 24,
    TogglePauseAllTts = 25,
    ToggleGiftPauseBypass = 26,
    ToggleFollowPauseBypass = 27,
    ToggleSharePauseBypass = 28,
    ToggleSubscriptionPauseBypass = 29,
    ToggleBroadcastOutput = 30
}

[Flags]
public enum GlobalShortcutModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public sealed class GlobalShortcutBinding
{
    public HotkeyAction Action { get; set; }
    public bool IsEnabled { get; set; }
    public string Gesture { get; set; } = string.Empty;

    public GlobalShortcutBinding Clone() => new()
    {
        Action = Action,
        IsEnabled = IsEnabled,
        Gesture = Gesture
    };
}

public sealed record GlobalShortcutDefinition(
    HotkeyAction Action,
    string DisplayName,
    string Description,
    string DefaultGesture,
    bool IsEnabledByDefault);

public readonly record struct GlobalShortcutGesture(
    GlobalShortcutModifiers Modifiers,
    string Key)
{
    private static readonly Dictionary<string, GlobalShortcutModifiers> ModifierAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Alt"] = GlobalShortcutModifiers.Alt,
            ["Control"] = GlobalShortcutModifiers.Control,
            ["Ctrl"] = GlobalShortcutModifiers.Control,
            ["Shift"] = GlobalShortcutModifiers.Shift,
            ["Windows"] = GlobalShortcutModifiers.Windows,
            ["Win"] = GlobalShortcutModifiers.Windows
        };

    private static readonly Dictionary<string, string> KeyAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = "Control",
            ["Control"] = "Control",
            ["Alt"] = "Alt",
            ["Shift"] = "Shift",
            ["Win"] = "Windows",
            ["Windows"] = "Windows",
            ["Esc"] = "Escape",
            ["Escape"] = "Escape",
            ["Return"] = "Enter",
            ["Enter"] = "Enter",
            ["Spacebar"] = "Space",
            ["Space"] = "Space",
            ["Back"] = "Backspace",
            ["Backspace"] = "Backspace",
            ["PgUp"] = "PageUp",
            ["PageUp"] = "PageUp",
            ["PgDn"] = "PageDown",
            ["PageDown"] = "PageDown",
            ["Del"] = "Delete",
            ["Delete"] = "Delete",
            ["Ins"] = "Insert",
            ["Insert"] = "Insert",
            ["Left"] = "Left",
            ["Right"] = "Right",
            ["Up"] = "Up",
            ["Down"] = "Down",
            ["Home"] = "Home",
            ["End"] = "End",
            ["Tab"] = "Tab",
            ["Pause"] = "Pause",
            ["CapsLock"] = "CapsLock",
            ["NumLock"] = "NumLock",
            ["ScrollLock"] = "ScrollLock"
        };

    public bool IsModifierOnly => Key is "Control" or "Alt" or "Shift" or "Windows";

    public bool IsSafeSystemWide =>
        IsModifierOnly ||
        Modifiers != GlobalShortcutModifiers.None ||
        Key.StartsWith('F');

    public string DisplayText
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(GlobalShortcutModifiers.Control)) parts.Add("Control");
            if (Modifiers.HasFlag(GlobalShortcutModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(GlobalShortcutModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(GlobalShortcutModifiers.Windows)) parts.Add("Windows");
            parts.Add(Key);
            return string.Join('+', parts);
        }
    }

    public static bool TryParse(
        string? value,
        out GlobalShortcutGesture gesture,
        out string error)
    {
        gesture = default;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Enter a shortcut or turn this shortcut off.";
            return false;
        }

        string[] parts = value
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Length > 5)
        {
            error = "Use a shortcut such as Control+Alt+S or Control.";
            return false;
        }

        GlobalShortcutModifiers modifiers = GlobalShortcutModifiers.None;
        for (int index = 0; index < parts.Length - 1; index++)
        {
            if (!ModifierAliases.TryGetValue(parts[index], out GlobalShortcutModifiers modifier))
            {
                error = $"{parts[index]} is not a supported modifier.";
                return false;
            }

            if ((modifiers & modifier) != 0)
            {
                error = $"{parts[index]} is repeated.";
                return false;
            }

            modifiers |= modifier;
        }

        if (!TryNormalizeKey(parts[^1], out string key))
        {
            error = $"{parts[^1]} is not a supported key.";
            return false;
        }

        if (ModifierAliases.TryGetValue(key, out GlobalShortcutModifiers keyModifier) &&
            (modifiers & keyModifier) != 0)
        {
            error = $"{key} cannot be both the key and a modifier.";
            return false;
        }

        gesture = new GlobalShortcutGesture(modifiers, key);
        if (!gesture.IsSafeSystemWide)
        {
            error = "Add Control, Alt, Shift, or Windows so normal typing is not taken over. Function keys may be used alone.";
            gesture = default;
            return false;
        }

        return true;
    }

    private static bool TryNormalizeKey(string value, out string key)
    {
        key = string.Empty;
        string trimmed = value.Trim();
        if (trimmed.Length == 1 && char.IsAsciiLetterOrDigit(trimmed[0]))
        {
            key = trimmed.ToUpperInvariant();
            return true;
        }

        if (trimmed.Length is 2 or 3 &&
            trimmed[0] is 'f' or 'F' &&
            int.TryParse(trimmed.AsSpan(1), out int functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            key = $"F{functionKey}";
            return true;
        }

        if (trimmed.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase) &&
            trimmed.Length == 7 &&
            char.IsAsciiDigit(trimmed[^1]))
        {
            key = $"Numpad{trimmed[^1]}";
            return true;
        }

        return KeyAliases.TryGetValue(trimmed, out key!);
    }
}

public static class GlobalShortcutCatalog
{
    public static IReadOnlyList<GlobalShortcutDefinition> Definitions { get; } =
    [
        new(HotkeyAction.StopBuiltInGuidance, "Stop built-in spoken guidance", "Immediately silences only SafeSpeak's built-in guidance voice. It does not stop livestream text to speech.", "Control", true),
        new(HotkeyAction.AnnounceStatus, "Hear SafeSpeak status", "Announces connection, armed state, playback mode, queue, current speech, and broadcast output.", "Control+Alt+S", true),
        new(HotkeyAction.ToggleArm, "Arm or disarm", "Toggles moderated livestream monitoring and text to speech.", "Control+Alt+A", true),
        new(HotkeyAction.EmergencyStop, "Emergency stop", "Stops stream speech, clears the queue, and disarms SafeSpeak.", "Control+Alt+P", true),
        new(HotkeyAction.StopCurrentSpeech, "Stop current stream speech", "Stops only the livestream message currently speaking.", "Control+Alt+K", true),
        new(HotkeyAction.ToggleAutomaticPlayback, "Toggle automatic or manual playback", "Switches between automatic playback and manual queue advance.", "", false),
        new(HotkeyAction.TogglePause, "Pause or resume stream TTS", "Pauses text to speech or resumes it in automatic mode.", "", false),
        new(HotkeyAction.SpeakNext, "Speak next approved message", "Speaks one queued message while manual playback is active.", "", false),
        new(HotkeyAction.ClearQueue, "Clear stream TTS queue", "Removes pending approved messages without stopping the current message.", "", false),
        new(HotkeyAction.ToggleConnection, "Connect or disconnect source", "Toggles the current livestream source connection.", "", false),
        new(HotkeyAction.ToggleSpokenGuidance, "Toggle built-in spoken guidance", "Turns SafeSpeak's own interface guidance voice on or off. External screen readers remain supported.", "", false),
        new(HotkeyAction.ToggleHighContrast, "Toggle high contrast", "Switches between the high-contrast and light themes.", "", false),
        new(HotkeyAction.CycleAudience, "Cycle chat audience", "Cycles through Everyone, Followers, Subscribers, and Moderators.", "", false),
        new(HotkeyAction.CycleModerationStrength, "Cycle moderation strength", "Cycles through the four local intent-moderation levels.", "", false),
        new(HotkeyAction.ToggleEnglishOnly, "Toggle English and Latin-script filter", "Toggles the English and Latin-script chat filter.", "", false),
        new(HotkeyAction.ToggleMixedScriptProtection, "Toggle mixed-script protection", "Toggles protection against words that mix writing systems to evade filtering.", "", false),
        new(HotkeyAction.ToggleDonorEligibility, "Toggle gift-sender eligibility", "Toggles whether current-stream gift senders can speak outside the selected audience tier.", "", false),
        new(HotkeyAction.ToggleChatAnnouncements, "Toggle chat announcements", "Toggles approved chat messages entering the speech queue.", "", false),
        new(HotkeyAction.ToggleGiftAnnouncements, "Toggle gift announcements", "Toggles spoken gift announcements.", "", false),
        new(HotkeyAction.ToggleFollowAnnouncements, "Toggle follow announcements", "Toggles spoken follow announcements.", "", false),
        new(HotkeyAction.ToggleShareAnnouncements, "Toggle share announcements", "Toggles spoken share announcements.", "", false),
        new(HotkeyAction.ToggleSubscriptionAnnouncements, "Toggle subscription announcements", "Toggles spoken subscription announcements.", "", false),
        new(HotkeyAction.ToggleJoinAnnouncements, "Toggle join announcements", "Toggles spoken viewer-join announcements.", "", false),
        new(HotkeyAction.ToggleLikeAnnouncements, "Toggle like announcements", "Toggles spoken like announcements.", "", false),
        new(HotkeyAction.TogglePauseAllTts, "Toggle pause all TTS", "Toggles whether pausing holds chat and every event announcement.", "", false),
        new(HotkeyAction.ToggleGiftPauseBypass, "Toggle gifts while paused", "Toggles whether gift announcements may speak while chat TTS is paused.", "", false),
        new(HotkeyAction.ToggleFollowPauseBypass, "Toggle follows while paused", "Toggles whether follow announcements may speak while chat TTS is paused.", "", false),
        new(HotkeyAction.ToggleSharePauseBypass, "Toggle shares while paused", "Toggles whether share announcements may speak while chat TTS is paused.", "", false),
        new(HotkeyAction.ToggleSubscriptionPauseBypass, "Toggle subscriptions while paused", "Toggles whether subscription announcements may speak while chat TTS is paused.", "", false),
        new(HotkeyAction.ToggleBroadcastOutput, "Toggle broadcast output", "Toggles whether approved livestream speech is sent to the selected broadcast device.", "", false)
    ];

    public static List<GlobalShortcutBinding> CreateDefaults() => Definitions
        .Select(definition => new GlobalShortcutBinding
        {
            Action = definition.Action,
            IsEnabled = definition.IsEnabledByDefault,
            Gesture = definition.DefaultGesture
        })
        .ToList();

    public static List<GlobalShortcutBinding> NormalizeBindings(
        IEnumerable<GlobalShortcutBinding>? bindings)
    {
        var supplied = (bindings ?? [])
            .Where(binding => binding is not null && Enum.IsDefined(binding.Action))
            .GroupBy(binding => binding.Action)
            .ToDictionary(group => group.Key, group => group.First());
        var usedGestures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<GlobalShortcutBinding>(Definitions.Count);

        foreach (GlobalShortcutDefinition definition in Definitions)
        {
            GlobalShortcutBinding binding = supplied.TryGetValue(definition.Action, out GlobalShortcutBinding? saved)
                ? saved.Clone()
                : new GlobalShortcutBinding
                {
                    Action = definition.Action,
                    IsEnabled = definition.IsEnabledByDefault,
                    Gesture = definition.DefaultGesture
                };

            binding.Action = definition.Action;
            binding.Gesture = (binding.Gesture ?? string.Empty).Trim();
            if (GlobalShortcutGesture.TryParse(binding.Gesture, out GlobalShortcutGesture gesture, out _))
            {
                binding.Gesture = gesture.DisplayText;
            }
            else if (binding.IsEnabled)
            {
                binding.IsEnabled = false;
            }

            if (binding.IsEnabled && !usedGestures.Add(binding.Gesture))
            {
                binding.IsEnabled = false;
            }

            normalized.Add(binding);
        }

        return normalized;
    }
}
