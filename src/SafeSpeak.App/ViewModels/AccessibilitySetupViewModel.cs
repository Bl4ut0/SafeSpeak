using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeSpeak.App.Services;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.AI;
using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Audio.VoiceFramework;
using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Models;

namespace SafeSpeak.App.ViewModels;

public enum AccessibilitySetupPage
{
    Reader,
    Theme,
    Platform,
    Voice,
    Filtering,
    Keybinds,
    Navigation,
    Review
}

public sealed record ThemeChoiceOption(
    ThemePreference Value,
    string Name,
    string Description,
    string AutomationName);

public sealed record KeybindDisplayItem(
    string Name,
    string Gesture,
    string Description);

public sealed record NavigationShortcutItem(
    string Category,
    string Gesture,
    string Description);

public sealed partial class AccessibilitySetupViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Action _onCompleted;
    private readonly bool _changeExistingProfile;
    private readonly bool _confirmationOnly;
    private readonly bool _previousAnnouncerState;
    private readonly AccessibilitySnapshot? _settingsRerunSnapshot;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private readonly KokoroModelManager _kokoroManager;
    private readonly VoicePackageManager _voicePackageManager;
    private readonly ModularTtsEngine _ttsEngine;
    private readonly WasapiAudioRouter _previewAudioRouter;
    private readonly PrivateVoicePreviewOutput _previewOutput;
    private readonly Qwen3GuardRuntimeManager _qwenRuntime;
    private CancellationTokenSource? _qwenInstallCts;

    private bool _completed;
    private bool _modelChecked;
    private bool _initialized;
    private bool _initialPromptAnnounced;
    private Task? _modelCheckTask;

    public AccessibilitySetupViewModel(
        AppSettings settings,
        ScreenReaderAnnouncer announcer,
        Action onCompleted,
        bool changeExistingProfile = false)
    {
        _settings = settings;
        _announcer = announcer;
        _onCompleted = onCompleted;
        _changeExistingProfile = changeExistingProfile;
        _confirmationOnly =
            !changeExistingProfile &&
            settings.HasCompletedOnboarding &&
            settings.IsAwaitingAccessibilityConfirmation;
        _previousAnnouncerState = announcer.IsEnhancedAccessibilityEnabled;
        _settingsRerunSnapshot = changeExistingProfile
            ? AccessibilitySnapshot.Capture(settings)
            : null;

        _kokoroManager = new KokoroModelManager();
        _voicePackageManager = new VoicePackageManager();
        _ttsEngine = new ModularTtsEngine(_kokoroManager, _voicePackageManager);
        _previewAudioRouter = new WasapiAudioRouter();
        _previewOutput = new PrivateVoicePreviewOutput(_ttsEngine, _previewAudioRouter);
        _qwenRuntime = new Qwen3GuardRuntimeManager();

        ThemeOptions =
        [
            new(
                ThemePreference.Light,
                "Light",
                "A bright theme with dark text and clear blue controls.",
                "Light theme, option 1 of 3"),
            new(
                ThemePreference.Dark,
                "Dark",
                "A dark theme with bright text and high-visibility controls.",
                "Dark theme, option 2 of 3"),
            new(
                ThemePreference.HighContrast,
                "High Contrast",
                "The strongest contrast with black, white, and yellow.",
                "High Contrast theme, option 3 of 3")
        ];

        SpokenGuidanceEnabled =
            _settings.EffectiveSpokenGuidance != SpokenGuidanceMode.Disabled;
        ThemePreference initialTheme =
            _settings.EffectiveTheme == ThemePreference.Unset
                ? ThemePreference.Light
                : _settings.EffectiveTheme;
        SelectedThemeOption =
            ThemeOptions.First(option => option.Value == initialTheme);

        // Connectors disabled by default on clean onboarding unless already configured
        UseTikFinity = _settings.ConfiguredSourceConnectorIds.Contains(
            TikFinityWebSocketClient.ConnectorDescriptor.Id,
            StringComparer.OrdinalIgnoreCase);
        UseTikTokDirect = _settings.ConfiguredSourceConnectorIds.Contains(
            TikTokLiveConnector.ConnectorDescriptor.Id,
            StringComparer.OrdinalIgnoreCase);
        TikTokUsername = _settings.TikTokUsername;

        AiClassificationEnabled = _settings.AiClassificationEnabled;
        SelectedModerationModel = _settings.ModerationModel;
        IgnoreChatReplies = _settings.IgnoreChatReplies;

        LoadVoices();
        PopulateKeybinds();
        PopulateNavigationShortcuts();

        AccessibilitySetupPage initialPage = _changeExistingProfile
            ? AccessibilitySetupPage.Reader
            : ResolveInitialPage();
        _currentPage = initialPage;
        _initialized = true;
        UpdatePagePresentation();
    }

    public ScreenReaderAnnouncer Announcer => _announcer;
    public ObservableCollection<ThemeChoiceOption> ThemeOptions { get; }
    public ObservableCollection<string> ReviewItems { get; } = [];
    public ObservableCollection<VoiceInfo> Voices { get; } = [];
    public ObservableCollection<KeybindDisplayItem> Keybinds { get; } = [];
    public ObservableCollection<NavigationShortcutItem> NavigationShortcuts { get; } = [];

    [ObservableProperty]
    private AccessibilitySetupPage _currentPage;

    [ObservableProperty]
    private bool _spokenGuidanceEnabled;

    [ObservableProperty]
    private ThemeChoiceOption _selectedThemeOption = null!;

    [ObservableProperty]
    private bool _useTikFinity;

    [ObservableProperty]
    private bool _useTikTokDirect;

    [ObservableProperty]
    private string _tikTokUsername = string.Empty;

    [ObservableProperty]
    private string _selectedVoice = string.Empty;

    [ObservableProperty]
    private VoiceInfo? _selectedVoiceInfo;

    [ObservableProperty]
    private bool _isDownloadingVoice;

    [ObservableProperty]
    private double _voiceDownloadProgress;

    [ObservableProperty]
    private bool _aiClassificationEnabled = true;

    [ObservableProperty]
    private bool _ignoreChatReplies;

    [ObservableProperty]
    private ModerationModelPreference _selectedModerationModel = ModerationModelPreference.BuiltInHybrid;

    [ObservableProperty]
    private bool _isDownloadingQwen;

    [ObservableProperty]
    private double _qwenDownloadProgress;

    [ObservableProperty]
    private string _qwenStatusText = "The optional Qwen3Guard model is not installed.";

    [ObservableProperty]
    private string _stepProgress = string.Empty;

    [ObservableProperty]
    private string _promptText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _modelStatus =
        "SafeSpeak has not checked the bundled language model yet.";

    [ObservableProperty]
    private string _primaryButtonText = "Continue";

    [ObservableProperty]
    private string _primaryButtonAutomationName = "Continue to the next setup step";

    [ObservableProperty]
    private string _keyboardHelpText =
        "Keyboard: use Tab and Shift plus Tab between controls. Use arrow keys inside a list.";

    [ObservableProperty]
    private bool _isBusy;

    public bool IsReaderStep => CurrentPage == AccessibilitySetupPage.Reader;
    public bool IsThemeStep => CurrentPage == AccessibilitySetupPage.Theme;
    public bool IsPlatformStep => CurrentPage == AccessibilitySetupPage.Platform;
    public bool IsVoiceStep => CurrentPage == AccessibilitySetupPage.Voice;
    public bool IsFilteringStep => CurrentPage == AccessibilitySetupPage.Filtering;
    public bool IsKeybindsStep => CurrentPage == AccessibilitySetupPage.Keybinds;
    public bool IsNavigationStep => CurrentPage == AccessibilitySetupPage.Navigation;
    public bool IsReviewStep => CurrentPage == AccessibilitySetupPage.Review;

    public bool IsKokoroInstalled => _kokoroManager.IsInstalled;
    public bool IsQwenInstalled => _qwenRuntime.IsModelInstalled;
    public bool IsQwenSelected => SelectedModerationModel == ModerationModelPreference.Qwen3Guard06BCompressed;
    public bool HasAnyConnectorSelected => UseTikFinity || UseTikTokDirect;
    public string TikFinityStatusBadge => UseTikFinity ? "Enabled" : "Disabled";
    public string TikTokDirectStatusBadge => UseTikTokDirect ? "Enabled" : "Disabled";

    partial void OnUseTikFinityChanged(bool value)
    {
        OnPropertyChanged(nameof(HasAnyConnectorSelected));
        OnPropertyChanged(nameof(TikFinityStatusBadge));
    }

    partial void OnUseTikTokDirectChanged(bool value)
    {
        OnPropertyChanged(nameof(HasAnyConnectorSelected));
        OnPropertyChanged(nameof(TikTokDirectStatusBadge));
    }

    public bool IsInteractionEnabled => !IsBusy;
    public bool IsBackAvailable => CurrentPage != AccessibilitySetupPage.Reader;
    public bool IsPrimaryButtonVisible => !IsReaderStep;

    public event EventHandler? FocusRequested;

    public void AnnounceInitialPrompt()
    {
        if (_initialPromptAnnounced) return;
        _initialPromptAnnounced = true;
        _announcer.IsEnhancedAccessibilityEnabled =
            !_settings.HasConfirmedAccessibilityPreferences ||
            _settings.IsSpokenGuidanceEnabled;
        AnnounceCurrentPage();
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        if (IsBusy) return;

        switch (CurrentPage)
        {
            case AccessibilitySetupPage.Reader:
                CompleteReaderStep(SpokenGuidanceEnabled);
                break;
            case AccessibilitySetupPage.Theme:
                CompleteThemeStep();
                break;
            case AccessibilitySetupPage.Platform:
                CompletePlatformStep();
                break;
            case AccessibilitySetupPage.Voice:
                CompleteVoiceStep();
                break;
            case AccessibilitySetupPage.Filtering:
                CompleteFilteringStep();
                break;
            case AccessibilitySetupPage.Keybinds:
                CompleteKeybindsStep();
                break;
            case AccessibilitySetupPage.Navigation:
                CompleteNavigationStep();
                break;
            case AccessibilitySetupPage.Review:
                await PrepareReviewAsync();
                if (!_modelChecked) return;
                CompleteOnboarding();
                break;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        if (!IsBackAvailable || IsBusy) return;

        AccessibilitySetupPage destination = CurrentPage switch
        {
            AccessibilitySetupPage.Theme => AccessibilitySetupPage.Reader,
            AccessibilitySetupPage.Platform => AccessibilitySetupPage.Theme,
            AccessibilitySetupPage.Voice => AccessibilitySetupPage.Platform,
            AccessibilitySetupPage.Filtering => AccessibilitySetupPage.Voice,
            AccessibilitySetupPage.Keybinds => AccessibilitySetupPage.Filtering,
            AccessibilitySetupPage.Navigation => AccessibilitySetupPage.Keybinds,
            AccessibilitySetupPage.Review => AccessibilitySetupPage.Navigation,
            _ => CurrentPage
        };
        NavigateTo(destination);
    }

    [RelayCommand]
    public void JumpToStep(object? parameter)
    {
        if (IsBusy || parameter is null) return;
        AccessibilitySetupPage page;
        if (parameter is AccessibilitySetupPage p)
        {
            page = p;
        }
        else if (int.TryParse(parameter.ToString(), out int stepNumber))
        {
            page = stepNumber switch
            {
                1 => AccessibilitySetupPage.Reader,
                2 => AccessibilitySetupPage.Theme,
                3 => AccessibilitySetupPage.Platform,
                4 => AccessibilitySetupPage.Voice,
                5 => AccessibilitySetupPage.Filtering,
                6 => AccessibilitySetupPage.Keybinds,
                7 => AccessibilitySetupPage.Navigation,
                8 => AccessibilitySetupPage.Review,
                _ => CurrentPage
            };
        }
        else if (Enum.TryParse<AccessibilitySetupPage>(parameter.ToString(), true, out var parsed))
        {
            page = parsed;
        }
        else
        {
            return;
        }

        if (CurrentPage == page) return;
        NavigateTo(page);
    }

    partial void OnSelectedThemeOptionChanged(ThemeChoiceOption value)
    {
        if (value is not null) ThemeManager.Apply(value.Value);
    }

    partial void OnSelectedVoiceChanged(string value)
    {
        SelectedVoiceInfo = Voices.FirstOrDefault(v => v.Id == value);
    }

    partial void OnSelectedModerationModelChanged(ModerationModelPreference value)
    {
        OnPropertyChanged(nameof(IsQwenSelected));
    }

    partial void OnIsBusyChanged(bool value) =>
        OnPropertyChanged(nameof(IsInteractionEnabled));

    partial void OnCurrentPageChanged(AccessibilitySetupPage value)
    {
        if (!_initialized) return;
        RaisePageProperties();
        UpdatePagePresentation();
    }

    private AccessibilitySetupPage ResolveInitialPage()
    {
        if (!_settings.HasConfirmedAccessibilityPreferences)
            return AccessibilitySetupPage.Reader;

        return _settings.OnboardingStage switch
        {
            OnboardingStage.Voice => AccessibilitySetupPage.Voice,
            OnboardingStage.Filtering => AccessibilitySetupPage.Filtering,
            OnboardingStage.Keybinds => AccessibilitySetupPage.Keybinds,
            OnboardingStage.Navigation => AccessibilitySetupPage.Navigation,
            OnboardingStage.Review => AccessibilitySetupPage.Review,
            OnboardingStage.Complete => AccessibilitySetupPage.Review,
            _ => AccessibilitySetupPage.Platform
        };
    }

    [RelayCommand]
    private void ChooseSpokenGuidanceYes() => CompleteReaderStep(enabled: true);

    [RelayCommand]
    private void ChooseSpokenGuidanceNo() => CompleteReaderStep(enabled: false);

    private void CompleteReaderStep(bool enabled)
    {
        SpokenGuidanceEnabled = enabled;
        _announcer.IsEnhancedAccessibilityEnabled = enabled;
        NavigateTo(AccessibilitySetupPage.Theme);
    }

    private void CompleteThemeStep()
    {
        AccessibilitySnapshot snapshot = AccessibilitySnapshot.Capture(_settings);
        SpokenGuidanceMode guidance = SpokenGuidanceEnabled
            ? SpokenGuidanceMode.Enabled
            : SpokenGuidanceMode.Disabled;

        AccessibilityPreferencesSelectionResult result =
            AccessibilityPreferencesConfirmation.Select(
                _settings,
                guidance,
                SelectedThemeOption.Value,
                applyImmediately: _changeExistingProfile);
        bool confirmationPending =
            result is AccessibilityPreferencesSelectionResult.ConfirmationPending or
            AccessibilityPreferencesSelectionResult.ChangedConfirmationPending;

        if (_changeExistingProfile)
            _settings.OnboardingStage = OnboardingStage.Platform;
        else if (_confirmationOnly)
            _settings.OnboardingStage = OnboardingStage.Complete;
        else if (confirmationPending)
            _settings.OnboardingStage = OnboardingStage.Platform;

        if (!_settings.TrySave(out string? error))
        {
            snapshot.Restore(_settings);
            ThemeManager.Apply(_settings.EffectiveTheme);
            ReportSaveFailure(error);
            return;
        }

        ThemeManager.Apply(_settings.EffectiveTheme);
        _announcer.IsEnhancedAccessibilityEnabled =
            _settings.EffectiveSpokenGuidance != SpokenGuidanceMode.Disabled;

        if (_confirmationOnly)
        {
            _completed = true;
            _announcer.Announce(
                confirmationPending
                    ? "Your updated Reader and Theme choices are saved. Confirm them the next time SafeSpeak launches. Opening SafeSpeak now."
                    : "Reader and Theme choices confirmed. Opening SafeSpeak.",
                interrupt: true);
            _onCompleted();
            return;
        }

        if (confirmationPending)
        {
            _announcer.Announce(
                "Reader and Theme choices saved. SafeSpeak will ask you to confirm them the next time it launches. Continuing setup now.",
                interrupt: true);
        }

        _announcer.IsEnhancedAccessibilityEnabled = _settings.IsSpokenGuidanceEnabled;
        NavigateTo(AccessibilitySetupPage.Platform);
    }

    private void CompletePlatformStep()
    {
        string directUsername = string.Empty;
        if (UseTikTokDirect &&
            !TikTokLiveConnector.TryNormalizeUsername(TikTokUsername, out directUsername))
        {
            StatusText = "TikTok Direct needs a valid creator username using letters, numbers, periods, or underscores.";
            _announcer.Announce(StatusText, interrupt: true);
            return;
        }

        _settings.ConfiguredSourceConnectorIds = [];
        if (UseTikFinity)
            _settings.ConfiguredSourceConnectorIds.Add(TikFinityWebSocketClient.ConnectorDescriptor.Id);
        if (UseTikTokDirect)
            _settings.ConfiguredSourceConnectorIds.Add(TikTokLiveConnector.ConnectorDescriptor.Id);

        _settings.SelectedSourceConnectorId =
            _settings.ConfiguredSourceConnectorIds.FirstOrDefault() ?? string.Empty;
        _settings.ActiveSourceConnectorIds = [.. _settings.ConfiguredSourceConnectorIds];
        _settings.AutoConnectSource = _settings.ActiveSourceConnectorIds.Count > 0;
        _settings.TikTokUsername = directUsername;
        _settings.LocalConnectorAutoDetectConsent = false;
        _settings.LocalConnectorDetectionStatus = OnboardingConnectorDetectionStatus.NotChecked;
        _settings.LocalConnectorDetectionSummary = "Local connector detection is not used during setup.";
        _settings.OnboardingStage = OnboardingStage.Voice;
        if (!_settings.TrySave(out string? error))
        {
            ReportSaveFailure(error);
            return;
        }

        NavigateTo(AccessibilitySetupPage.Voice);
    }

    private void CompleteVoiceStep()
    {
        if (!string.IsNullOrWhiteSpace(SelectedVoice))
        {
            _settings.SelectedVoiceName = SelectedVoice;
        }
        _settings.OnboardingStage = OnboardingStage.Filtering;
        if (!_settings.TrySave(out string? error))
        {
            ReportSaveFailure(error);
            return;
        }

        NavigateTo(AccessibilitySetupPage.Filtering);
    }

    private void CompleteFilteringStep()
    {
        _settings.AiClassificationEnabled = AiClassificationEnabled;
        _settings.ModerationModel = SelectedModerationModel;
        _settings.IgnoreChatReplies = IgnoreChatReplies;
        _settings.OnboardingStage = OnboardingStage.Keybinds;
        if (!_settings.TrySave(out string? error))
        {
            ReportSaveFailure(error);
            return;
        }

        NavigateTo(AccessibilitySetupPage.Keybinds);
    }

    private void CompleteKeybindsStep()
    {
        _settings.OnboardingStage = OnboardingStage.Navigation;
        if (!_settings.TrySave(out string? error))
        {
            ReportSaveFailure(error);
            return;
        }

        NavigateTo(AccessibilitySetupPage.Navigation);
    }

    private void CompleteNavigationStep()
    {
        _settings.OnboardingStage = OnboardingStage.Review;
        if (!_settings.TrySave(out string? error))
        {
            ReportSaveFailure(error);
            return;
        }

        NavigateTo(AccessibilitySetupPage.Review);
    }

    private void CompleteOnboarding()
    {
        OnboardingStage previousStage = _settings.OnboardingStage;
        _settings.OnboardingStage = OnboardingStage.Complete;
        if (!_settings.TrySave(out string? error))
        {
            _settings.OnboardingStage = previousStage;
            ReportSaveFailure(error);
            return;
        }

        _completed = true;
        ThemeManager.Apply(_settings.EffectiveTheme);
        string confirmationReminder = _settings.IsAwaitingAccessibilityConfirmation
            ? " Reader and Theme will be confirmed the next time SafeSpeak launches."
            : string.Empty;
        _announcer.Announce(
            $"Setup complete. SafeSpeak is ready.{confirmationReminder} It remains disarmed until you choose Arm SafeSpeak.",
            interrupt: true);
        _announcer.IsEnhancedAccessibilityEnabled = _settings.IsSpokenGuidanceEnabled;
        _onCompleted();
    }

    [RelayCommand]
    public void RestartSetup()
    {
        _settings.ResetOnboarding();
        _settings.TrySave(out _);
        SpokenGuidanceEnabled = false;
        _announcer.IsEnhancedAccessibilityEnabled = true;
        UseTikFinity = false;
        UseTikTokDirect = false;
        TikTokUsername = string.Empty;
        SelectedThemeOption = ThemeOptions.First(o => o.Value == ThemePreference.Light);
        NavigateTo(AccessibilitySetupPage.Reader);
        _announcer.Announce("Setup restarted. Step 1 of 8. Do you want to use the SafeSpeak built-in screen reader? Press Y for Yes or N for No.", interrupt: true);
    }

    private void NavigateTo(AccessibilitySetupPage page)
    {
        if (CurrentPage == page) return;
        CurrentPage = page;
        FocusRequested?.Invoke(this, EventArgs.Empty);
        AnnounceCurrentPage();
    }

    private void LoadVoices()
    {
        Voices.Clear();
        foreach (VoiceInfo voice in _ttsEngine.GetAvailableVoices())
        {
            Voices.Add(voice);
        }

        if (Voices.Count > 0)
        {
            SelectedVoice = Voices.Any(v => v.Id == _settings.SelectedVoiceName)
                ? (_settings.SelectedVoiceName ?? Voices[0].Id)
                : Voices[0].Id;
            SelectedVoiceInfo = Voices.FirstOrDefault(v => v.Id == SelectedVoice);
        }
    }

    [RelayCommand]
    public async Task TestVoiceAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedVoice)) return;

        VoiceInfo? voice = Voices.FirstOrDefault(v => v.Id == SelectedVoice);
        string name = voice?.DisplayName ?? "the selected voice";
        string sample = $"This is {name}. SafeSpeak voice preview is working.";

        _announcer.StopSpeaking();
        try
        {
            await _previewOutput.SpeakAsync(sample, interrupt: true);
        }
        catch (Exception ex)
        {
            _announcer.Announce($"Voice preview failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task InstallKokoroAsync()
    {
        if (IsDownloadingVoice || IsKokoroInstalled) return;

        IsDownloadingVoice = true;
        VoiceDownloadProgress = 0;
        _announcer.Announce("Installing Kokoro offline voices. This download is about 330 megabytes.", interrupt: true);
        try
        {
            var progress = new Progress<double>(value => VoiceDownloadProgress = value);
            await _kokoroManager.InstallAsync(progress);
            LoadVoices();
            SelectedVoice = KokoroModelManager.VoicePrefix + "af_heart";
            OnPropertyChanged(nameof(IsKokoroInstalled));
            _announcer.Announce("Kokoro voices installed successfully. Twenty seven offline neural voices are now available.", interrupt: true);
        }
        catch (Exception ex)
        {
            _announcer.Announce($"Kokoro installation failed: {ex.Message}", interrupt: true);
        }
        finally
        {
            IsDownloadingVoice = false;
        }
    }

    [RelayCommand]
    public void OpenWindowsSpeechSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:speech") { UseShellExecute = true });
            _announcer.Announce("Opened Windows Speech Settings. Under Manage voices, you can download additional language packs.", interrupt: true);
        }
        catch (Exception ex)
        {
            _announcer.Announce($"Could not open Windows Speech settings: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task InstallQwenAsync()
    {
        if (IsDownloadingQwen || IsQwenInstalled) return;

        if (!_qwenRuntime.IsRuntimeAvailable)
        {
            QwenStatusText = "The packaged local model runtime is missing. The built-in filter remains active.";
            _announcer.Announce(QwenStatusText, interrupt: true);
            return;
        }

        _qwenInstallCts?.Dispose();
        _qwenInstallCts = new CancellationTokenSource();
        IsDownloadingQwen = true;
        QwenDownloadProgress = 0;
        QwenStatusText = "Starting Qwen3Guard download (~484 MB). SafeSpeak remains usable during the download.";
        _announcer.Announce(QwenStatusText, interrupt: true);

        var progress = new Progress<QwenModelInstallProgress>(update =>
        {
            QwenDownloadProgress = update.Percent;
            QwenStatusText = update.Status;
        });

        try
        {
            await _qwenRuntime.InstallModelAsync(progress, _qwenInstallCts.Token);
            OnPropertyChanged(nameof(IsQwenInstalled));
            QwenStatusText = "The optional Qwen3Guard model is installed and ready.";
            _announcer.Announce("Qwen3Guard model installed successfully.", interrupt: true);
        }
        catch (OperationCanceledException)
        {
            QwenStatusText = "Qwen3Guard installation was cancelled.";
            _announcer.Announce(QwenStatusText, interrupt: true);
        }
        catch (Exception ex)
        {
            QwenStatusText = $"Qwen3Guard installation failed: {ex.Message}";
            _announcer.Announce(QwenStatusText, interrupt: true);
        }
        finally
        {
            IsDownloadingQwen = false;
        }
    }

    [RelayCommand]
    public void CancelQwenInstall()
    {
        _qwenInstallCts?.Cancel();
    }

    private void PopulateKeybinds()
    {
        Keybinds.Clear();
        Keybinds.Add(new("Hear SafeSpeak status", "Control + Shift + S", "Privately announces arming state, message queue count, and connector status."));
        Keybinds.Add(new("Arm / Disarm SafeSpeak", "Control + Shift + A", "Begins or pauses reading live chat aloud without closing your streaming app."));
        Keybinds.Add(new("Emergency Stop", "Pause / Break or Control + Shift + X", "Immediately silences speech and clears all pending chat and announcement queues."));
        Keybinds.Add(new("Shut up live speech", "Control + Shift + Q", "Silences the message currently speaking on your livestream audio track."));
        Keybinds.Add(new("Built-in screen reader silence", "Control key alone", "Pressing the Control key immediately silences SafeSpeak's built-in spoken guidance."));
    }

    private void PopulateNavigationShortcuts()
    {
        NavigationShortcuts.Clear();
        NavigationShortcuts.Add(new("Pages", "Control + 1", "Open the Live monitoring and queue page."));
        NavigationShortcuts.Add(new("Pages", "Control + 2", "Open the Safety and moderation settings page."));
        NavigationShortcuts.Add(new("Pages", "Control + 3", "Open the Voice selection and audio output page."));
        NavigationShortcuts.Add(new("Pages", "Control + 4", "Open the Settings and configuration page."));
        NavigationShortcuts.Add(new("Chapters", "Alt + 1 through Alt + 9", "Jump directly to any numbered chapter on the active page."));
        NavigationShortcuts.Add(new("Setup", "Control + 1 through Control + 8", "Jump directly to any setup step during onboarding."));
    }

    private void UpdatePagePresentation()
    {
        switch (CurrentPage)
        {
            case AccessibilitySetupPage.Reader:
                ConfigureReaderPage();
                break;
            case AccessibilitySetupPage.Theme:
                ConfigureThemePage();
                break;
            case AccessibilitySetupPage.Platform:
                ConfigurePlatformPage();
                break;
            case AccessibilitySetupPage.Voice:
                ConfigureVoicePage();
                break;
            case AccessibilitySetupPage.Filtering:
                ConfigureFilteringPage();
                break;
            case AccessibilitySetupPage.Keybinds:
                ConfigureKeybindsPage();
                break;
            case AccessibilitySetupPage.Navigation:
                ConfigureNavigationPage();
                break;
            case AccessibilitySetupPage.Review:
                ConfigureReviewPage();
                break;
        }

        RaisePageProperties();
    }

    private void ConfigureReaderPage()
    {
        StepProgress = _changeExistingProfile
            ? "Step 1 of 8 — run setup again"
            : _settings.IsAwaitingAccessibilityConfirmation
                ? "Step 1 of 8 — confirmation 2 of 2"
                : "Step 1 of 8 — selection 1 of 2";
        PromptText = "Do you want to use the SafeSpeak built-in screen reader?";
        if (_changeExistingProfile)
            StatusText =
                "Choose Yes to use SafeSpeak spoken guidance or No to continue without it. Windows Narrator, NVDA, and JAWS remain supported either way.";
        else if (_settings.IsAwaitingAccessibilityConfirmation)
            StatusText =
                $"Last time you chose {AccessibilityPreferencesConfirmation.GetDisplayName(_settings.PendingSpokenGuidance)}. Choose Yes or No again, then confirm the theme in Step 2.";
        else
            StatusText =
                "Choose Yes to hear SafeSpeak describe focused controls, or No to continue without SafeSpeak speech. External screen readers remain supported either way.";

        PrimaryButtonText = "Continue";
        PrimaryButtonAutomationName = "Continue from the screen reader question";
        KeyboardHelpText =
            "Keyboard: press Y for Yes or N for No. Enter activates the focused answer. Either answer continues to Theme.";
    }

    private void ConfigureThemePage()
    {
        StepProgress = _changeExistingProfile
            ? "Step 2 of 8 — run setup again"
            : _settings.IsAwaitingAccessibilityConfirmation
                ? "Step 2 of 8 — confirmation 2 of 2"
                : "Step 2 of 8 — selection 1 of 2";
        PromptText = "Choose your visual theme";
        if (_changeExistingProfile)
            StatusText =
                "Choose Light, Dark, or High Contrast. Reader and Theme are independent and apply immediately after you save this step.";
        else if (_settings.IsAwaitingAccessibilityConfirmation)
            StatusText =
                $"Last time you chose the {AccessibilityPreferencesConfirmation.GetDisplayName(_settings.PendingTheme)} theme. Choose the same combination to confirm it, or choose a different theme to start a new confirmation.";
        else
            StatusText =
                "Choose Light, Dark, or High Contrast. SafeSpeak accepts these choices now and asks you to confirm them the next time it launches.";

        PrimaryButtonText = "Save and continue (Y)";
        PrimaryButtonAutomationName = "Save Reader and Theme choices and continue";
        KeyboardHelpText =
            "Keyboard: Tab once to the Theme selector without changing the selection. Use Arrow keys to hear and choose Light, Dark, or High Contrast. Press Y to save and continue.";
    }

    private void ConfigurePlatformPage()
    {
        StepProgress = "Step 3 of 8";
        PromptText = "Configure your streaming connections";
        StatusText =
            "Select which connectors to enable. All connectors are disabled by default. You can also continue without enabling any connectors and configure them later in Settings.";
        PrimaryButtonText = "Continue (Y)";
        PrimaryButtonAutomationName = "Save streaming connection and continue";
        KeyboardHelpText =
            "Keyboard: Tab through the connector choices and username. Space changes a checkbox. Press Y to save and continue.";
    }

    private void ConfigureVoicePage()
    {
        StepProgress = "Step 4 of 8";
        PromptText = "Choose your voice and quality level";
        StatusText =
            "Select the voice that will speak approved livestream chat messages. Windows includes Level 1 (Desktop) and Level 2 (Natural) voices immediately. You can install high-fidelity Level 3 neural voices (Kokoro, ~330 MB) or select custom voices anytime.";
        PrimaryButtonText = "Continue (Y)";
        PrimaryButtonAutomationName = "Save voice selection and continue";
        KeyboardHelpText =
            "Keyboard: Tab to the voice dropdown and use Arrow keys to choose a voice. Tab to Test Voice to hear it. Press Y to save and continue.";
    }

    private void ConfigureFilteringPage()
    {
        StepProgress = "Step 5 of 8";
        PromptText = "Configure on-device AI moderation";
        StatusText =
            "This step is educational; there is no choice to make. The bundled model complements Unicode-aware rules, banned words, and moderation strictness. No model download or cloud account is required. SafeSpeak automatically uses its bundled on-device language model with deterministic rules and banned terms. You can also install the upgraded Qwen3Guard 0.6B local model for higher contextual understanding.";
        PrimaryButtonText = "Continue (Y)";
        PrimaryButtonAutomationName = "Continue after learning how enhanced filtering works";
        KeyboardHelpText =
            "Keyboard: read the explanation, then press Y or Tab to Continue. The model status is also exposed as a polite screen-reader announcement.";
        if (!_modelChecked) _ = EnsureModelStatusAsync();
    }

    private void ConfigureKeybindsPage()
    {
        StepProgress = "Step 6 of 8";
        PromptText = "Review primary global hotkeys";
        StatusText =
            "Global shortcuts work system-wide even when other applications have focus. Control plus Shift plus S announces status privately. Control plus Shift plus A arms or disarms speech. Pause or Control plus Shift plus X triggers emergency stop. Press Control alone to silence built-in guidance.";
        PrimaryButtonText = "Continue (Y)";
        PrimaryButtonAutomationName = "Continue after reviewing global hotkeys";
        KeyboardHelpText =
            "Keyboard: Tab through the keybind reference list. Press Y to continue.";
    }

    private void ConfigureNavigationPage()
    {
        StepProgress = "Step 7 of 8";
        PromptText = "Learn application navigation shortcuts";
        StatusText =
            "Use Control plus 1, 2, 3, and 4 to switch between Live, Safety, Voice, and Settings tabs. Use Alt plus 1 through 9 to jump directly to any chapter on the active page. During setup, use Control plus 1 through 8 to jump between setup steps.";
        PrimaryButtonText = "Continue (Y)";
        PrimaryButtonAutomationName = "Continue after reviewing navigation shortcuts";
        KeyboardHelpText =
            "Keyboard: Tab through the navigation shortcut list. Press Y to continue to the final review.";
    }

    private void ConfigureReviewPage()
    {
        StepProgress = "Step 8 of 8";
        PromptText = "Review your SafeSpeak setup";
        StatusText =
            "Use the arrow keys in the review list to hear each saved choice. Built-in Windows speech voices (Levels 1 & 2) are ready immediately; you can install high-fidelity Level 3 neural voices in the Voice tab anytime. Finish Setup saves the result and opens SafeSpeak.";
        PrimaryButtonText = "Finish setup (Y)";
        PrimaryButtonAutomationName = "Save setup and open SafeSpeak";
        KeyboardHelpText =
            "Keyboard: press Y to finish and open SafeSpeak, or press N to restart setup. You can also press Control plus 1 through 8 to revisit any step.";

        if (_modelChecked)
            BuildReviewItems();
        else
        {
            ReviewItems.Clear();
            ReviewItems.Add("Language filtering: verification is in progress.");
            _ = PrepareReviewAsync();
        }
    }

    private Task EnsureModelStatusAsync()
    {
        if (_modelChecked || _lifetimeCancellation.IsCancellationRequested)
            return Task.CompletedTask;

        return _modelCheckTask ??= RefreshModelStatusAsync();
    }

    private async Task RefreshModelStatusAsync()
    {
        IsBusy = true;
        ModelStatus = "Checking the bundled on-device moderation model.";
        try
        {
            ModelCheckResult result = await Task.Run(
                () =>
                {
                    using var classifier = new LocalOnnxIntentClassifier();
                    return new ModelCheckResult(
                        classifier.IsModelLoaded,
                        classifier.AvailabilityMessage);
                },
                _lifetimeCancellation.Token);
            _modelChecked = true;
            ModelStatus = result.IsLoaded
                ? "Enhanced filtering is installed and active. The bundled MiniLM model runs on this computer."
                : $"{result.AvailabilityMessage} Deterministic filtering and banned terms remain active.";
            _announcer.Announce(ModelStatus);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _modelChecked = true;
            ModelStatus =
                $"The bundled model could not be verified. Deterministic filtering and banned terms remain active. {ex.Message}";
            _announcer.Announce(ModelStatus, interrupt: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PrepareReviewAsync()
    {
        await EnsureModelStatusAsync();
        if (CurrentPage == AccessibilitySetupPage.Review &&
            _modelChecked &&
            !_lifetimeCancellation.IsCancellationRequested)
        {
            BuildReviewItems();
        }
    }

    private void BuildReviewItems()
    {
        ReviewItems.Clear();
        ReviewItems.Add(
            $"Built-in spoken guidance: {(SpokenGuidanceEnabled ? "On" : "Off")}");
        ReviewItems.Add($"Visual theme: {SelectedThemeOption.Name}");

        string connectorsSummary = HasAnyConnectorSelected
            ? string.Join(", ", new[]
            {
                UseTikFinity ? "TikFinity" : null,
                UseTikTokDirect ? $"TikTok Direct (@{TikTokUsername})" : null
            }.Where(name => name is not null))
            : "None configured (will configure in Settings later)";
        ReviewItems.Add($"Configured connectors: {connectorsSummary}. Each can be turned on or off from Live.");

        string voiceDisplayName = SelectedVoiceInfo?.DisplayName ?? SelectedVoice;
        if (string.IsNullOrWhiteSpace(voiceDisplayName)) voiceDisplayName = "Default Windows voice";
        ReviewItems.Add($"Speech voice: {voiceDisplayName} (Level {SelectedVoiceInfo?.ComputeLevel ?? 1})");

        ReviewItems.Add($"Language filtering: {ModelStatus}");
        if (IsQwenSelected)
        {
            ReviewItems.Add($"Contextual AI model: Qwen3Guard 0.6B ({(IsQwenInstalled ? "Installed" : "Not yet installed")})");
        }
        ReviewItems.Add($"Chat @replies from viewers: {(IgnoreChatReplies ? "Ignored (suppressed from TTS)" : "Allowed (spoken)")}");
        ReviewItems.Add("Global shortcuts: Status (Ctrl+Shift+S), Arm (Ctrl+Shift+A), Emergency Stop (Pause / Ctrl+Shift+X), Silence (Ctrl+Shift+Q).");
        ReviewItems.Add("Navigation: Tabs (Ctrl+1..4), Chapters (Alt+1..0), Setup steps (Ctrl+1..8).");
        ReviewItems.Add("SafeSpeak opens disarmed and does not process chat until you choose Arm SafeSpeak.");
    }

    private void ReportSaveFailure(string? error)
    {
        StatusText =
            $"SafeSpeak could not save this setup step. Nothing from this step was committed. {error}";
        _announcer.Announce(
            "SafeSpeak could not save this setup step. Nothing from this step was committed. Please try again.",
            interrupt: true);
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AnnounceCurrentPage()
    {
        if (CurrentPage == AccessibilitySetupPage.Review)
        {
            BuildReviewItems();
            string reviewSummary = string.Join(". ", ReviewItems);
            _announcer.Announce(
                $"{StepProgress}. {PromptText}. Press Y to confirm your settings and open SafeSpeak, or press N to restart setup. You can also press Control plus 1 through 8 to revisit any step. Here is your configuration summary: {reviewSummary}.",
                interrupt: true);
        }
        else
        {
            _announcer.Announce(
                $"{StepProgress}. {PromptText}. {StatusText} {KeyboardHelpText}",
                interrupt: true);
        }
    }

    private void RaisePageProperties()
    {
        OnPropertyChanged(nameof(IsReaderStep));
        OnPropertyChanged(nameof(IsThemeStep));
        OnPropertyChanged(nameof(IsPlatformStep));
        OnPropertyChanged(nameof(IsVoiceStep));
        OnPropertyChanged(nameof(IsFilteringStep));
        OnPropertyChanged(nameof(IsKeybindsStep));
        OnPropertyChanged(nameof(IsNavigationStep));
        OnPropertyChanged(nameof(IsReviewStep));
        OnPropertyChanged(nameof(IsBackAvailable));
        OnPropertyChanged(nameof(IsPrimaryButtonVisible));
        OnPropertyChanged(nameof(HasAnyConnectorSelected));
    }

    public void Dispose()
    {
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _qwenInstallCts?.Cancel();
        _qwenInstallCts?.Dispose();
        _previewOutput.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _previewAudioRouter.Dispose();
        _ttsEngine.Dispose();
        if (!_completed)
        {
            if (_settingsRerunSnapshot is { } snapshot)
            {
                snapshot.Restore(_settings);
                _settings.TrySave(out _);
            }

            ThemeManager.Apply(_settings.EffectiveTheme);
            _announcer.IsEnhancedAccessibilityEnabled = _previousAnnouncerState;
        }
    }

    private sealed record ModelCheckResult(bool IsLoaded, string AvailabilityMessage);

    private readonly record struct AccessibilitySnapshot(
        SpokenGuidanceMode SpokenGuidance,
        ThemePreference Theme,
        SpokenGuidanceMode PendingSpokenGuidance,
        ThemePreference PendingTheme,
        OnboardingStage OnboardingStage,
        string SelectedSourceConnectorId,
        string[] ConfiguredSourceConnectorIds,
        string[] ActiveSourceConnectorIds,
        string TikTokUsername,
        bool AutoConnectSource,
        bool LocalConnectorAutoDetectConsent,
        OnboardingConnectorDetectionStatus LocalConnectorDetectionStatus,
        string LocalConnectorDetectionSummary,
        bool AiClassificationEnabled,
        bool IgnoreChatReplies,
        string SelectedVoiceName,
        ModerationModelPreference ModerationModel)
    {
        public static AccessibilitySnapshot Capture(AppSettings settings) =>
            new(
                settings.SpokenGuidance,
                settings.Theme,
                settings.PendingSpokenGuidance,
                settings.PendingTheme,
                settings.OnboardingStage,
                settings.SelectedSourceConnectorId,
                [.. settings.ConfiguredSourceConnectorIds],
                [.. settings.ActiveSourceConnectorIds],
                settings.TikTokUsername,
                settings.AutoConnectSource,
                settings.LocalConnectorAutoDetectConsent,
                settings.LocalConnectorDetectionStatus,
                settings.LocalConnectorDetectionSummary,
                settings.AiClassificationEnabled,
                settings.IgnoreChatReplies,
                settings.SelectedVoiceName ?? string.Empty,
                settings.ModerationModel);

        public void Restore(AppSettings settings)
        {
            settings.SpokenGuidance = SpokenGuidance;
            settings.Theme = Theme;
            settings.PendingSpokenGuidance = PendingSpokenGuidance;
            settings.PendingTheme = PendingTheme;
            settings.OnboardingStage = OnboardingStage;
            settings.SelectedSourceConnectorId = SelectedSourceConnectorId;
            settings.ConfiguredSourceConnectorIds = [.. ConfiguredSourceConnectorIds];
            settings.ActiveSourceConnectorIds = [.. ActiveSourceConnectorIds];
            settings.TikTokUsername = TikTokUsername;
            settings.AutoConnectSource = AutoConnectSource;
            settings.LocalConnectorAutoDetectConsent = LocalConnectorAutoDetectConsent;
            settings.LocalConnectorDetectionStatus = LocalConnectorDetectionStatus;
            settings.LocalConnectorDetectionSummary = LocalConnectorDetectionSummary;
            settings.AiClassificationEnabled = AiClassificationEnabled;
            settings.IgnoreChatReplies = IgnoreChatReplies;
            settings.SelectedVoiceName = SelectedVoiceName;
            settings.ModerationModel = ModerationModel;
        }
    }
}
