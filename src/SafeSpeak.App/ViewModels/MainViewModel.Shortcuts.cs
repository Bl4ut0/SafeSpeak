using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Models;

namespace SafeSpeak.App.ViewModels;

public sealed class GlobalShortcutEditorViewModel : ObservableObject
{
    private bool _isEnabled;
    private string _gesture;
    private string _status;

    public GlobalShortcutEditorViewModel(
        GlobalShortcutDefinition definition,
        GlobalShortcutBinding binding)
    {
        Definition = definition;
        _isEnabled = binding.IsEnabled;
        _gesture = binding.Gesture;
        _status = binding.IsEnabled ? "Waiting for Windows registration." : "Disabled.";
    }

    public GlobalShortcutDefinition Definition { get; }
    public HotkeyAction Action => Definition.Action;
    public string DisplayName => Definition.DisplayName;
    public string Description => Definition.Description;
    public string EnabledAutomationName => $"Enable global shortcut for {DisplayName}";
    public string GestureAutomationName => $"Global shortcut keys for {DisplayName}";

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string Gesture
    {
        get => _gesture;
        set => SetProperty(ref _gesture, value ?? string.Empty);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public GlobalShortcutBinding ToBinding() => new()
    {
        Action = Action,
        IsEnabled = IsEnabled,
        Gesture = Gesture
    };

    public void ResetToDefault()
    {
        IsEnabled = Definition.IsEnabledByDefault;
        Gesture = Definition.DefaultGesture;
    }

    public override string ToString() => DisplayName;
}

public sealed partial class MainViewModel
{
    private static readonly IReadOnlyDictionary<string, string> ReservedApplicationShortcuts =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Control+1"] = "open Live",
            ["Control+2"] = "open Safety",
            ["Control+3"] = "open Voice",
            ["Control+4"] = "open Settings"
        };

    private bool _isUpdatingGlobalShortcutEditors;
    private int _settingsGuidePageIndex;

    [ObservableProperty]
    private bool _areSettingsGuideControlsAtTop = true;

    [ObservableProperty]
    private GlobalShortcutEditorViewModel? _selectedGlobalShortcut;

    [ObservableProperty]
    private string _globalShortcutStatus =
        "Global shortcuts have not been registered with Windows yet.";

    public ObservableCollection<GlobalShortcutEditorViewModel> GlobalShortcutEditors { get; } = new();
    public IReadOnlyList<string> ApplicationKeyboardShortcuts { get; } =
    [
        "Control+1: Live page",
        "Control+2: Safety page",
        "Control+3: Voice page",
        "Control+4: Settings page"
    ];

    public IReadOnlyList<string> SettingsGuidePages { get; } =
    [
        "Page 1, theme. The Theme selector is one keyboard stop. Use Left or Right Arrow to choose Light, Dark, or High Contrast. The selected theme is announced immediately; Tab continues without changing it.",
        "Page 2, built-in guidance. Choose whether SafeSpeak speaks focused controls, its private output device, volume from zero to one hundred fifty percent, speech speed, detailed or short descriptions, and typed-character echo. Test guidance and Shut up affect only SafeSpeak guidance, never livestream text to speech.",
        "Page 3, keyboard shortcuts. Global shortcuts work outside SafeSpeak while it is running. Choose an action, enable it, record the desired key combination, and apply it. The only fixed application shortcuts are Control plus 1, 2, 3, or 4 to open the four main pages.",
        "Page 4, queue and spam limits. The queue limit sets how many approved messages may wait. Optional rolling spam limits separately control messages from one viewer and messages across the whole stream, counted per one second or per ten seconds. Their saved values remain adjustable while the limits are off.",
        "Page 5, language and audience. These controls decide which writing systems and viewer groups are eligible before a message can enter speech. Moderation still applies to every eligible message.",
        "Page 6, stream announcements and pause behavior. Choose which event types may speak and which events may continue when chat speech is paused. Emergency Stop always stops and clears all livestream speech.",
        "Page 7, local audit logs. Logging is optional and off until enabled. Logs may contain usernames, raw chat, gifts, and moderation decisions. The Open logs folder button shows where those files are stored.",
        "Page 8, interface and setup. Interface text size ranges from one hundred to two hundred percent. Run Setup Again reopens the guided setup and preserves current settings if it is cancelled."
    ];

    public string SettingsGuideNarration =>
        "Settings guide. " + string.Join(" ", SettingsGuidePages);
    public bool AreSettingsGuideControlsAtEnd => !AreSettingsGuideControlsAtTop;
    public string SettingsGuideVisibilityButtonText =>
        AreSettingsGuideControlsAtTop ? "Move controls to bottom" : "Move controls to top";
    public string SettingsGuideVisibilityButtonAutomationName =>
        AreSettingsGuideControlsAtTop
            ? "Move the Settings guide controls to the bottom of the page"
            : "Move the Settings guide controls to the top of the page";

    [RelayCommand]
    public void ReadSettingsGuide() =>
        AnnounceNarration(
            SettingsGuideNarration,
            "Reading the Settings guide. Use Shut up built-in guidance to stop it.",
            interrupt: true);

    [RelayCommand]
    public void ReadNextSettingsGuidePage()
    {
        if (_settingsGuidePageIndex >= SettingsGuidePages.Count)
        {
            _settingsGuidePageIndex = 0;
        }

        int spokenPosition = _settingsGuidePageIndex + 1;
        string page = SettingsGuidePages[_settingsGuidePageIndex];
        _settingsGuidePageIndex++;
        AnnounceNarration(
            page,
            $"Playing Settings guide page {spokenPosition} of {SettingsGuidePages.Count}.",
            interrupt: true);
    }

    [RelayCommand]
    public void RestartSettingsGuide()
    {
        _settingsGuidePageIndex = 0;
        AnnounceState("Settings guide reset to page 1. Choose Play guide page when ready.");
    }

    [RelayCommand]
    public void ToggleSettingsGuideVisibility()
    {
        AreSettingsGuideControlsAtTop = !AreSettingsGuideControlsAtTop;
        AnnounceState(AreSettingsGuideControlsAtTop
            ? "Settings guide controls moved to the top of the page. The button you pressed moved with them and is now labeled Move controls to bottom. Navigate to the top of the Settings page to find it again."
            : "Settings guide controls moved to the bottom of the page. The button you pressed moved with them and is now labeled Move controls to top. Navigate to the bottom of the Settings page to find it again. The guide text remains visible.");
    }

    partial void OnAreSettingsGuideControlsAtTopChanged(bool value)
    {
        OnPropertyChanged(nameof(AreSettingsGuideControlsAtEnd));
        OnPropertyChanged(nameof(SettingsGuideVisibilityButtonText));
        OnPropertyChanged(nameof(SettingsGuideVisibilityButtonAutomationName));
    }

    [RelayCommand]
    public void ReadFixedApplicationShortcuts() =>
        AnnounceNarration(
            "Fixed SafeSpeak application shortcuts. " +
            string.Join(". ", ApplicationKeyboardShortcuts) + ".",
            "Reading the four fixed page shortcuts.",
            interrupt: true);

    public event EventHandler? GlobalShortcutsChanged;

    public string SelectedGlobalShortcutDescription => SelectedGlobalShortcut is null
        ? "Choose an action to configure its system-wide shortcut."
        : $"{SelectedGlobalShortcut.Description} Current setting: " +
          (SelectedGlobalShortcut.IsEnabled
              ? SelectedGlobalShortcut.Gesture
              : "disabled") + ".";

    public string SelectedGlobalShortcutAccessibleText
    {
        get
        {
            if (SelectedGlobalShortcut is null)
            {
                return "No shortcut action selected.";
            }

            int index = GlobalShortcutEditors.IndexOf(SelectedGlobalShortcut);
            string position = index >= 0
                ? $", option {index + 1} of {GlobalShortcutEditors.Count}"
                : string.Empty;
            return $"Shortcut action: {SelectedGlobalShortcut.DisplayName}{position}. " +
                   SelectedGlobalShortcutDescription;
        }
    }

    public string HearStatusHelpText =>
        "First control in the window and always available on every page. Announces source connection, armed and playback state, queue, current speech, and broadcast output through SafeSpeak built-in guidance on its selected audio device. " +
        ShortcutSentence(HotkeyAction.AnnounceStatus);

    public string ArmToggleHelpText =>
        "Press Enter or Space to arm or disarm. When armed, approved messages play automatically. " +
        ShortcutSentence(HotkeyAction.ToggleArm);

    public string EmergencyStopHelpText =>
        "Immediately stops all stream audio, clears the queue, and disarms SafeSpeak. Re-arm SafeSpeak to resume monitoring and speech. " +
        ShortcutSentence(HotkeyAction.EmergencyStop);

    public string StopCurrentSpeechAutomationName =>
        "Stop current stream speech button. " + ShortcutSentence(HotkeyAction.StopCurrentSpeech);

    public string StopBuiltInGuidanceHelpText =>
        "Immediately silences only SafeSpeak built-in spoken guidance. Livestream text to speech continues. " +
        ShortcutSentence(HotkeyAction.StopBuiltInGuidance) +
        " The separate Stream Deck action is named Stop Guidance.";

    partial void OnSelectedGlobalShortcutChanged(GlobalShortcutEditorViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedGlobalShortcutDescription));
        OnPropertyChanged(nameof(SelectedGlobalShortcutAccessibleText));
        // Focus narration and UI Automation announce the new ComboBox value.
        // Avoid queuing a second, competing announcement here.
    }

    private void InitializeGlobalShortcuts()
    {
        GlobalShortcutEditors.Clear();
        IReadOnlyDictionary<HotkeyAction, GlobalShortcutBinding> saved = _settings.GlobalShortcuts
            .ToDictionary(binding => binding.Action);

        foreach (GlobalShortcutDefinition definition in GlobalShortcutCatalog.Definitions)
        {
            GlobalShortcutBinding binding = saved.TryGetValue(
                definition.Action,
                out GlobalShortcutBinding? configured)
                ? configured
                : new GlobalShortcutBinding
                {
                    Action = definition.Action,
                    IsEnabled = definition.IsEnabledByDefault,
                    Gesture = definition.DefaultGesture
                };
            var editor = new GlobalShortcutEditorViewModel(definition, binding);
            editor.PropertyChanged += GlobalShortcutEditor_PropertyChanged;
            GlobalShortcutEditors.Add(editor);
        }

        SelectedGlobalShortcut = GlobalShortcutEditors.FirstOrDefault();
    }

    private void GlobalShortcutEditor_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_isUpdatingGlobalShortcutEditors ||
            e.PropertyName is nameof(GlobalShortcutEditorViewModel.Status))
        {
            return;
        }

        GlobalShortcutStatus =
            "Shortcut changes are not active yet. Choose Apply shortcut changes.";
        OnPropertyChanged(nameof(SelectedGlobalShortcutDescription));
        OnPropertyChanged(nameof(SelectedGlobalShortcutAccessibleText));
    }

    public IReadOnlyList<GlobalShortcutBinding> GetGlobalShortcutBindings() =>
        _settings.GlobalShortcuts.Select(binding => binding.Clone()).ToArray();

    [RelayCommand]
    public void ApplyGlobalShortcuts()
    {
        if (!TryValidateGlobalShortcuts(out List<GlobalShortcutBinding> bindings))
        {
            AnnounceState(GlobalShortcutStatus, interrupt: true);
            return;
        }

        _settings.GlobalShortcuts = bindings;
        SaveSettingsOrReport();
        NotifyShortcutHelpChanged();
        GlobalShortcutsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void ResetSelectedGlobalShortcut()
    {
        if (SelectedGlobalShortcut is null) return;

        _isUpdatingGlobalShortcutEditors = true;
        try
        {
            SelectedGlobalShortcut.ResetToDefault();
            SelectedGlobalShortcut.Status = "Default restored; choose Apply shortcut changes.";
        }
        finally
        {
            _isUpdatingGlobalShortcutEditors = false;
        }

        GlobalShortcutStatus =
            $"Default restored for {SelectedGlobalShortcut.DisplayName}. Choose Apply shortcut changes.";
        OnPropertyChanged(nameof(SelectedGlobalShortcutDescription));
        OnPropertyChanged(nameof(SelectedGlobalShortcutAccessibleText));
    }

    [RelayCommand]
    public void RestoreDefaultGlobalShortcuts()
    {
        _isUpdatingGlobalShortcutEditors = true;
        try
        {
            foreach (GlobalShortcutEditorViewModel editor in GlobalShortcutEditors)
            {
                editor.ResetToDefault();
                editor.Status = editor.IsEnabled
                    ? "Default restored; choose Apply shortcut changes."
                    : "Disabled by default.";
            }
        }
        finally
        {
            _isUpdatingGlobalShortcutEditors = false;
        }

        GlobalShortcutStatus =
            "All shortcut defaults restored. Choose Apply shortcut changes.";
        OnPropertyChanged(nameof(SelectedGlobalShortcutDescription));
        OnPropertyChanged(nameof(SelectedGlobalShortcutAccessibleText));
    }

    public void ReportGlobalShortcutRegistration(
        HotkeyRegistrationResult result,
        bool announce)
    {
        ArgumentNullException.ThrowIfNull(result);
        _isUpdatingGlobalShortcutEditors = true;
        try
        {
            foreach (GlobalShortcutEditorViewModel editor in GlobalShortcutEditors)
            {
                HotkeyRegistrationIssue? issue = result.Unavailable
                    .FirstOrDefault(candidate => candidate.Action == editor.Action);
                GlobalShortcutBinding? active = result.Registered
                    .FirstOrDefault(candidate => candidate.Action == editor.Action);
                editor.Status = issue is not null
                    ? $"Unavailable: {issue.Reason}"
                    : active is not null
                        ? $"Active globally: {active.Gesture}."
                        : "Disabled.";
            }
        }
        finally
        {
            _isUpdatingGlobalShortcutEditors = false;
        }

        GlobalShortcutStatus = result.AllRegistered
            ? $"{result.Registered.Count} shortcuts are active system-wide while SafeSpeak is running."
            : $"{result.Registered.Count} shortcuts are active. {result.Unavailable.Count} could not be registered; select an action to hear its status.";

        if (announce)
        {
            AnnounceState(GlobalShortcutStatus, interrupt: true);
        }
    }

    [RelayCommand]
    public void StopBuiltInGuidance()
    {
        _announcer.StopSpeaking();
        LiveStatusAnnouncement = "SafeSpeak built-in spoken guidance stopped.";
    }

    public async Task ExecuteGlobalShortcutAsync(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.StopBuiltInGuidance:
                StopBuiltInGuidance();
                break;
            case HotkeyAction.AnnounceStatus:
                AnnounceStatusPrivately();
                break;
            case HotkeyAction.ToggleArm:
                ToggleArm();
                break;
            case HotkeyAction.EmergencyStop:
                EmergencyStop();
                break;
            case HotkeyAction.StopCurrentSpeech:
                StopCurrentSpeech();
                break;
            case HotkeyAction.ToggleAutomaticPlayback:
                if (_ttsQueue.Mode == TtsPlaybackMode.Automatic) UseManualPlayback();
                else UseAutomaticPlayback();
                break;
            case HotkeyAction.TogglePause:
                PauseOrResumeTts();
                break;
            case HotkeyAction.SpeakNext:
                await SpeakNextApprovedMessage();
                break;
            case HotkeyAction.ClearQueue:
                ClearQueue();
                break;
            case HotkeyAction.ToggleConnection:
                if (IsConnected) await DisconnectTikFinity();
                else await ConnectTikFinity();
                break;
            case HotkeyAction.ToggleSpokenGuidance:
                SpokenGuidanceEnabled = !SpokenGuidanceEnabled;
                break;
            case HotkeyAction.ToggleHighContrast:
                SelectedTheme = SelectedTheme == ThemePreference.HighContrast
                    ? ThemePreference.Light
                    : ThemePreference.HighContrast;
                break;
            case HotkeyAction.CycleAudience:
                SelectedAudienceMode = SelectedAudienceMode switch
                {
                    AudienceMode.All => AudienceMode.FollowersOnly,
                    AudienceMode.FollowersOnly => AudienceMode.SubscribersOnly,
                    AudienceMode.SubscribersOnly => AudienceMode.ModeratorsOnly,
                    _ => AudienceMode.All
                };
                break;
            case HotkeyAction.CycleModerationStrength:
                ModerationLevel = ModerationLevel >= 4 ? 1 : ModerationLevel + 1;
                break;
            case HotkeyAction.ToggleEnglishOnly:
                EnglishOnly = !EnglishOnly;
                break;
            case HotkeyAction.ToggleMixedScriptProtection:
                RejectMixedScripts = !RejectMixedScripts;
                break;
            case HotkeyAction.ToggleDonorEligibility:
                AllowDonorsToSpeak = !AllowDonorsToSpeak;
                break;
            case HotkeyAction.ToggleChatAnnouncements:
                AnnounceChatMessages = !AnnounceChatMessages;
                AnnounceShortcutToggle("Chat announcements", AnnounceChatMessages);
                break;
            case HotkeyAction.ToggleGiftAnnouncements:
                AnnounceGifts = !AnnounceGifts;
                AnnounceShortcutToggle("Gift announcements", AnnounceGifts);
                break;
            case HotkeyAction.ToggleFollowAnnouncements:
                AnnounceFollows = !AnnounceFollows;
                AnnounceShortcutToggle("Follow announcements", AnnounceFollows);
                break;
            case HotkeyAction.ToggleShareAnnouncements:
                AnnounceShares = !AnnounceShares;
                AnnounceShortcutToggle("Share announcements", AnnounceShares);
                break;
            case HotkeyAction.ToggleSubscriptionAnnouncements:
                AnnounceSubscriptions = !AnnounceSubscriptions;
                AnnounceShortcutToggle("Subscription announcements", AnnounceSubscriptions);
                break;
            case HotkeyAction.ToggleJoinAnnouncements:
                AnnounceJoins = !AnnounceJoins;
                AnnounceShortcutToggle("Join announcements", AnnounceJoins);
                break;
            case HotkeyAction.ToggleLikeAnnouncements:
                AnnounceLikes = !AnnounceLikes;
                AnnounceShortcutToggle("Like announcements", AnnounceLikes);
                break;
            case HotkeyAction.TogglePauseAllTts:
                PauseAllTtsWhilePaused = !PauseAllTtsWhilePaused;
                break;
            case HotkeyAction.ToggleGiftPauseBypass:
                AllowGiftAnnouncementsWhilePaused = !AllowGiftAnnouncementsWhilePaused;
                break;
            case HotkeyAction.ToggleFollowPauseBypass:
                AllowFollowAnnouncementsWhilePaused = !AllowFollowAnnouncementsWhilePaused;
                break;
            case HotkeyAction.ToggleSharePauseBypass:
                AllowShareAnnouncementsWhilePaused = !AllowShareAnnouncementsWhilePaused;
                break;
            case HotkeyAction.ToggleSubscriptionPauseBypass:
                AllowSubscriptionAnnouncementsWhilePaused = !AllowSubscriptionAnnouncementsWhilePaused;
                break;
            case HotkeyAction.ToggleBroadcastOutput:
                BroadcastOutputEnabled = !BroadcastOutputEnabled;
                AnnounceShortcutToggle("Broadcast output", BroadcastOutputEnabled);
                break;
        }
    }

    private bool TryValidateGlobalShortcuts(
        out List<GlobalShortcutBinding> bindings)
    {
        bindings = GlobalShortcutEditors.Select(editor => editor.ToBinding()).ToList();
        var gestures = new Dictionary<string, GlobalShortcutEditorViewModel>(
            StringComparer.OrdinalIgnoreCase);
        var invalid = new HashSet<GlobalShortcutEditorViewModel>();

        _isUpdatingGlobalShortcutEditors = true;
        try
        {
            foreach (GlobalShortcutEditorViewModel editor in GlobalShortcutEditors)
            {
                if (!editor.IsEnabled)
                {
                    editor.Status = "Disabled.";
                    continue;
                }

                if (!GlobalShortcutGesture.TryParse(
                        editor.Gesture,
                        out GlobalShortcutGesture gesture,
                        out string error))
                {
                    editor.Status = $"Cannot apply: {error}";
                    invalid.Add(editor);
                    continue;
                }

                editor.Gesture = gesture.DisplayText;
                if (ReservedApplicationShortcuts.TryGetValue(
                        gesture.DisplayText,
                        out string? reservedPurpose))
                {
                    editor.Status = $"Cannot apply: {gesture.DisplayText} is reserved to {reservedPurpose}.";
                    invalid.Add(editor);
                    continue;
                }

                if (gestures.TryGetValue(
                        gesture.DisplayText,
                        out GlobalShortcutEditorViewModel? existing))
                {
                    editor.Status = $"Cannot apply: also used by {existing.DisplayName}.";
                    existing.Status = $"Cannot apply: also used by {editor.DisplayName}.";
                    invalid.Add(editor);
                    invalid.Add(existing);
                    continue;
                }

                gestures[gesture.DisplayText] = editor;
                editor.Status = "Ready to register with Windows.";
            }
        }
        finally
        {
            _isUpdatingGlobalShortcutEditors = false;
        }

        if (invalid.Count > 0)
        {
            GlobalShortcutStatus =
                $"Shortcut changes were not applied. Fix {invalid.Count} invalid or conflicting shortcut settings.";
            return false;
        }

        bindings = GlobalShortcutEditors.Select(editor => editor.ToBinding()).ToList();
        return true;
    }

    private string ShortcutSentence(HotkeyAction action)
    {
        GlobalShortcutBinding? binding = _settings.GlobalShortcuts
            .FirstOrDefault(candidate => candidate.Action == action);
        return binding is { IsEnabled: true } &&
               !string.IsNullOrWhiteSpace(binding.Gesture)
            ? $"Global shortcut: {binding.Gesture}."
            : "No global shortcut is assigned.";
    }

    private void NotifyShortcutHelpChanged()
    {
        OnPropertyChanged(nameof(HearStatusHelpText));
        OnPropertyChanged(nameof(ArmToggleHelpText));
        OnPropertyChanged(nameof(EmergencyStopHelpText));
        OnPropertyChanged(nameof(StopCurrentSpeechAutomationName));
        OnPropertyChanged(nameof(StopBuiltInGuidanceHelpText));
    }

    private void AnnounceShortcutToggle(string name, bool enabled) =>
        AnnounceState($"{name} {(enabled ? "enabled" : "disabled")}.");
}
