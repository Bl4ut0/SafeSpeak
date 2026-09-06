using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class GlobalShortcutTests
{
    [Theory]
    [InlineData("ctrl+alt+s", GlobalShortcutModifiers.Control | GlobalShortcutModifiers.Alt, "S", "Control+Alt+S")]
    [InlineData("alt+q", GlobalShortcutModifiers.Alt, "Q", "Alt+Q")]
    [InlineData("Control", GlobalShortcutModifiers.None, "Control", "Control")]
    [InlineData("shift+f12", GlobalShortcutModifiers.Shift, "F12", "Shift+F12")]
    [InlineData("win+PgDn", GlobalShortcutModifiers.Windows, "PageDown", "Windows+PageDown")]
    public void GestureParser_NormalizesSupportedGlobalShortcuts(
        string value,
        GlobalShortcutModifiers expectedModifiers,
        string expectedKey,
        string expectedDisplay)
    {
        bool parsed = GlobalShortcutGesture.TryParse(
            value,
            out GlobalShortcutGesture gesture,
            out string error);

        Assert.True(parsed, error);
        Assert.Equal(expectedModifiers, gesture.Modifiers);
        Assert.Equal(expectedKey, gesture.Key);
        Assert.Equal(expectedDisplay, gesture.DisplayText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Control+Control")]
    [InlineData("Command+S")]
    [InlineData("Control+VolumeUp")]
    public void GestureParser_RejectsUnsafeOrUnsupportedValues(string value)
    {
        Assert.False(GlobalShortcutGesture.TryParse(
            value,
            out _,
            out string error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Defaults_PreserveLegacyHotkeysAndGiveGuidanceSilencePlainControl()
    {
        IReadOnlyDictionary<HotkeyAction, GlobalShortcutBinding> defaults =
            GlobalShortcutCatalog.CreateDefaults()
                .ToDictionary(binding => binding.Action);

        AssertDefault(HotkeyAction.StopBuiltInGuidance, "Control");
        AssertDefault(HotkeyAction.AnnounceStatus, "Control+Alt+S");
        AssertDefault(HotkeyAction.EmergencyStop, "Control+Alt+P");
        AssertDefault(HotkeyAction.ToggleArm, "Control+Alt+A");
        AssertDefault(HotkeyAction.StopCurrentSpeech, "Control+Alt+K");
        Assert.Equal(
            Enum.GetValues<HotkeyAction>().Length,
            GlobalShortcutCatalog.Definitions.Count);

        void AssertDefault(HotkeyAction action, string gesture)
        {
            Assert.True(defaults[action].IsEnabled);
            Assert.Equal(gesture, defaults[action].Gesture);
        }
    }

    [Fact]
    public void NormalizeBindings_DisablesInvalidAndDuplicateEnabledBindings()
    {
        List<GlobalShortcutBinding> normalized =
            GlobalShortcutCatalog.NormalizeBindings(
            [
                new()
                {
                    Action = HotkeyAction.AnnounceStatus,
                    IsEnabled = true,
                    Gesture = "ctrl+shift+q"
                },
                new()
                {
                    Action = HotkeyAction.ToggleArm,
                    IsEnabled = true,
                    Gesture = "Control+Shift+Q"
                },
                new()
                {
                    Action = HotkeyAction.EmergencyStop,
                    IsEnabled = true,
                    Gesture = "letter"
                }
            ]);

        Assert.True(Find(HotkeyAction.AnnounceStatus).IsEnabled);
        Assert.Equal("Control+Shift+Q", Find(HotkeyAction.AnnounceStatus).Gesture);
        Assert.False(Find(HotkeyAction.ToggleArm).IsEnabled);
        Assert.False(Find(HotkeyAction.EmergencyStop).IsEnabled);
        Assert.Equal(
            GlobalShortcutCatalog.Definitions.Count,
            normalized.Count);

        GlobalShortcutBinding Find(HotkeyAction action) =>
            normalized.Single(binding => binding.Action == action);
    }

    [Fact]
    public void Settings_CustomGlobalShortcutRoundTrips()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "SafeSpeak.Core.Tests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
        try
        {
            AppSettings settings = AppSettings.Load(path);
            GlobalShortcutBinding arm = settings.GlobalShortcuts
                .Single(binding => binding.Action == HotkeyAction.ToggleArm);
            arm.Gesture = "Control+Shift+F9";
            arm.IsEnabled = true;

            Assert.True(settings.TrySave(out string? saveError), saveError);

            AppSettings reloaded = AppSettings.Load(path);
            GlobalShortcutBinding reloadedArm = reloaded.GlobalShortcuts
                .Single(binding => binding.Action == HotkeyAction.ToggleArm);
            Assert.True(reloadedArm.IsEnabled);
            Assert.Equal("Control+Shift+F9", reloadedArm.Gesture);
            Assert.Equal(AppSettings.CurrentSettingsSchemaVersion, reloaded.SettingsSchemaVersion);
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Settings_PreShortcutSchemaMigratesAllCurrentActions()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "SafeSpeak.Core.Tests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"SettingsSchemaVersion\":5}");

            AppSettings settings = AppSettings.Load(path);

            Assert.Equal(
                Enum.GetValues<HotkeyAction>().Length,
                settings.GlobalShortcuts.Count);
            Assert.Equal(
                "Control",
                settings.GlobalShortcuts.Single(binding =>
                    binding.Action == HotkeyAction.StopBuiltInGuidance).Gesture);
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
