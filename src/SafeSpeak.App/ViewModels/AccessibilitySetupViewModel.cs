using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.AI;
using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Models;

namespace SafeSpeak.App.ViewModels;

public enum AccessibilitySetupPage
{
    Reader,
    Theme,
    Platform,
    Filtering,
    Review,
    RestartRequired
}

public sealed record ThemeChoiceOption(
    ThemePreference Value,
    string Name,
    string Description,
    string AutomationName);

public sealed partial class AccessibilitySetupViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly LocalConnectorDetector _connectorDetector = new();
    private readonly Action _onCompleted;
    private readonly Action _onRestartRequired;
    private readonly bool _changeExistingProfile;
    private readonly bool _previousAnnouncerState;
    private readonly AccessibilitySnapshot? _settingsRerunSnapshot;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _completed;
    private bool _modelChecked;
    private bool _initialized;
    private bool _initialPromptAnnounced;
    private Task? _modelCheckTask;

    public AccessibilitySetupViewModel(
        AppSettings settings,
        ScreenReaderAnnouncer announcer,
        Action onCompleted,
        Action onRestartRequired,
        bool changeExistingProfile = false)
    {
        _settings = settings;
        _announcer = announcer;
        _onCompleted = onCompleted;
        _onRestartRequired = onRestartRequired;
        _changeExistingProfile = changeExistingProfile;
        _previousAnnouncerState = announcer.IsEnhancedAccessibilityEnabled;
        _settingsRerunSnapshot = changeExistingProfile
            ? AccessibilitySnapshot.Capture(settings)
            : null;

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
        UseTikFinity = _settings.AutoConnectSource && string.Equals(
            _settings.SelectedSourceConnectorId,
            TikFinityWebSocketClient.ConnectorDescriptor.Id,
            StringComparison.OrdinalIgnoreCase);
        AutoDetectLocalConnectors =
            _settings.LocalConnectorAutoDetectConsent;
        RestorePersistedDetectionResult();

        AccessibilitySetupPage initialPage = _changeExistingProfile
            ? AccessibilitySetupPage.Reader
            : ResolveInitialPage();
        _currentPage = initialPage;
        _initialized = true;
        UpdatePagePresentation();
    }

    public ScreenReaderAnnouncer Announcer => _announcer;
    public ObservableCollection<ThemeChoiceOption> ThemeOptions { get; }
    public ObservableCollection<LocalConnectorDetectionResult> DetectionResults { get; } = [];
    public ObservableCollection<string> ReviewItems { get; } = [];

    [ObservableProperty]
    private AccessibilitySetupPage _currentPage;

    [ObservableProperty]
    private bool _spokenGuidanceEnabled;

    [ObservableProperty]
    private ThemeChoiceOption _selectedThemeOption = null!;

    [ObservableProperty]
    private bool _useTikFinity = true;

    [ObservableProperty]
    private bool _autoDetectLocalConnectors;

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
    private string _primaryButtonText = "_Continue";

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
    public bool IsFilteringStep => CurrentPage == AccessibilitySetupPage.Filtering;
    public bool IsReviewStep => CurrentPage == AccessibilitySetupPage.Review;
    public bool IsRestartRequired => CurrentPage == AccessibilitySetupPage.RestartRequired;
    public bool IsInteractionEnabled => !IsBusy;
    public bool IsBackAvailable =>
        CurrentPage switch
        {
            AccessibilitySetupPage.Theme or
            AccessibilitySetupPage.Platform or
            AccessibilitySetupPage.Filtering or
            AccessibilitySetupPage.Review => true,
            _ => false
        };
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
                await CompletePlatformStepAsync();
                break;
            case AccessibilitySetupPage.Filtering:
                CompleteFilteringStep();
                break;
            case AccessibilitySetupPage.Review:
                await PrepareReviewAsync();
                if (!_modelChecked) return;
                CompleteOnboarding();
                break;
            case AccessibilitySetupPage.RestartRequired:
                _onRestartRequired();
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
            AccessibilitySetupPage.Filtering => AccessibilitySetupPage.Platform,
            AccessibilitySetupPage.Review => AccessibilitySetupPage.Filtering,
            _ => CurrentPage
        };
        NavigateTo(destination);
    }

    partial void OnSelectedThemeOptionChanged(ThemeChoiceOption value)
    {
        if (value is not null) ThemeManager.Apply(value.Value);
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
            OnboardingStage.Filtering => AccessibilitySetupPage.Filtering,
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
        if (_changeExistingProfile)
            _settings.OnboardingStage = OnboardingStage.Platform;

        if (!_settings.TrySave(out string? error))
        {
            snapshot.Restore(_settings);
            ThemeManager.Apply(_settings.EffectiveTheme);
            ReportSaveFailure(error);
            return;
        }

        ThemeManager.Apply(_settings.EffectiveTheme);
        if (result is AccessibilityPreferencesSelectionResult.RestartRequired or
            AccessibilityPreferencesSelectionResult.ChangedRestartRequired)
        {
            NavigateTo(AccessibilitySetupPage.RestartRequired);
            return;
        }

        _announcer.IsEnhancedAccessibilityEnabled = _settings.IsSpokenGuidanceEnabled;
        NavigateTo(AccessibilitySetupPage.Platform);
    }

    private async Task CompletePlatformStepAsync()
    {
        string previousConnector = _settings.SelectedSourceConnectorId;
        bool previousAutoConnect = _settings.AutoConnectSource;
        bool previousDetectionConsent =
            _settings.LocalConnectorAutoDetectConsent;
        OnboardingConnectorDetectionStatus previousDetectionStatus =
            _settings.LocalConnectorDetectionStatus;
        string previousDetectionSummary =
            _settings.LocalConnectorDetectionSummary;
        OnboardingStage previousStage = _settings.OnboardingStage;

        DetectionResults.Clear();
        if (UseTikFinity && AutoDetectLocalConnectors)
        {
            IsBusy = true;
            StatusText =
                "Checking only approved local TikFinity process names and the local test connector port.";
            _announcer.Announce(
                "Checking for TikFinity on this computer. SafeSpeak will not sign in, open a connection, or scan files.",
                interrupt: true);
            try
            {
                IReadOnlyList<LocalConnectorDetectionResult> results =
                    await _connectorDetector.DetectAsync(
                        userConsented: true,
                        _lifetimeCancellation.Token);
                foreach (LocalConnectorDetectionResult result in results)
                    DetectionResults.Add(result);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                IsBusy = false;
            }
        }

        _settings.SelectedSourceConnectorId =
            TikFinityWebSocketClient.ConnectorDescriptor.Id;
        _settings.AutoConnectSource = UseTikFinity;
        _settings.LocalConnectorAutoDetectConsent =
            AutoDetectLocalConnectors;
        ApplyDetectionResultToSettings(
            DetectionResults.FirstOrDefault());
        _settings.OnboardingStage = OnboardingStage.Filtering;
        if (!_settings.TrySave(out string? error))
        {
            _settings.SelectedSourceConnectorId = previousConnector;
            _settings.AutoConnectSource = previousAutoConnect;
            _settings.LocalConnectorAutoDetectConsent =
                previousDetectionConsent;
            _settings.LocalConnectorDetectionStatus =
                previousDetectionStatus;
            _settings.LocalConnectorDetectionSummary =
                previousDetectionSummary;
            _settings.OnboardingStage = previousStage;
            ReportSaveFailure(error);
            return;
        }

        NavigateTo(AccessibilitySetupPage.Filtering);
    }

    private void CompleteFilteringStep()
    {
        OnboardingStage previousStage = _settings.OnboardingStage;
        bool previousAiSetting = _settings.AiClassificationEnabled;
        _settings.AiClassificationEnabled = true;
        _settings.OnboardingStage = OnboardingStage.Review;
        if (!_settings.TrySave(out string? error))
        {
            _settings.AiClassificationEnabled = previousAiSetting;
            _settings.OnboardingStage = previousStage;
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
        _announcer.Announce(
            "Setup complete. SafeSpeak is ready. It remains disarmed until you choose Arm SafeSpeak.",
            interrupt: true);
        _announcer.IsEnhancedAccessibilityEnabled = _settings.IsSpokenGuidanceEnabled;
        _onCompleted();
    }

    private void NavigateTo(AccessibilitySetupPage page)
    {
        if (CurrentPage == page) return;
        CurrentPage = page;
        FocusRequested?.Invoke(this, EventArgs.Empty);
        AnnounceCurrentPage();
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
                StepProgress = "Step 3 of 5";
                PromptText = "Choose your streaming connection";
                StatusText =
                    "TikFinity is the supported TikTok connection in this release. SafeSpeak can check for it locally only after you select the consent checkbox.";
                PrimaryButtonText = "_Continue";
                PrimaryButtonAutomationName = "Save streaming connection and continue";
                KeyboardHelpText =
                    "Keyboard: Tab through the platform and detection checkboxes. Space changes a checkbox.";
                break;
            case AccessibilitySetupPage.Filtering:
                StepProgress = "Step 4 of 5";
                PromptText = "Enhanced language filtering";
                StatusText =
                    "SafeSpeak automatically uses its bundled on-device language model with deterministic rules and banned terms. It does not send chat to a cloud moderation service.";
                PrimaryButtonText = "_Continue";
                PrimaryButtonAutomationName = "Accept automatic enhanced filtering and continue";
                KeyboardHelpText =
                    "Keyboard: Tab to Continue. The model status is also exposed as a polite screen-reader announcement.";
                if (!_modelChecked) _ = EnsureModelStatusAsync();
                break;
            case AccessibilitySetupPage.Review:
                StepProgress = "Step 5 of 5";
                PromptText = "Review your SafeSpeak setup";
                StatusText =
                    "Use the arrow keys in the review list to hear each saved choice. Finish Setup saves the result and opens SafeSpeak.";
                PrimaryButtonText = "_Finish setup";
                PrimaryButtonAutomationName = "Save setup and open SafeSpeak";
                KeyboardHelpText =
                    "Keyboard: use arrow keys in the review list, then Tab to Finish Setup.";
                if (_modelChecked)
                    BuildReviewItems();
                else
                {
                    ReviewItems.Clear();
                    ReviewItems.Add(
                        "Language filtering: verification is in progress.");
                    _ = PrepareReviewAsync();
                }
                break;
            case AccessibilitySetupPage.RestartRequired:
                StepProgress = "Reader and theme confirmation 1 of 2 saved";
                PromptText =
                    $"{AccessibilityPreferencesConfirmation.GetDisplayName(_settings.PendingSpokenGuidance)} and {AccessibilityPreferencesConfirmation.GetDisplayName(_settings.PendingTheme)} are saved.";
                StatusText =
                    "Close SafeSpeak, reopen it, then answer Step 1 Reader and Step 2 Theme the same way. Choose the same combination to confirm it. A different combination becomes a new first choice. After confirmation, setup continues with your streaming platform.";
                PrimaryButtonText = "_Close SafeSpeak";
                PrimaryButtonAutomationName =
                    "Close SafeSpeak so accessibility choices can be confirmed after reopening";
                KeyboardHelpText =
                    "Keyboard: press Tab to reach Close SafeSpeak, then reopen the app.";
                break;
        }

        RaisePageProperties();
    }

    private void ConfigureReaderPage()
    {
        StepProgress = _changeExistingProfile
            ? "Step 1 of 5 — run setup again"
            : _settings.IsAwaitingAccessibilityConfirmation
                ? "Step 1 of 5 — confirmation 2 of 2"
                : "Step 1 of 5 — selection 1 of 2";
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

        PrimaryButtonText = "_Continue";
        PrimaryButtonAutomationName = "Continue from the screen reader question";
        KeyboardHelpText =
            "Keyboard: press Y for Yes or N for No. Enter activates the focused answer. Either answer continues to Theme.";
    }

    private void ConfigureThemePage()
    {
        StepProgress = _changeExistingProfile
            ? "Step 2 of 5 — run setup again"
            : _settings.IsAwaitingAccessibilityConfirmation
                ? "Step 2 of 5 — confirmation 2 of 2"
                : "Step 2 of 5 — selection 1 of 2";
        PromptText = "Choose your visual theme";
        if (_changeExistingProfile)
            StatusText =
                "Choose Light, Dark, or High Contrast. Reader and Theme are independent and apply immediately after you save this step.";
        else if (_settings.IsAwaitingAccessibilityConfirmation)
            StatusText =
                $"Last time you chose the {AccessibilityPreferencesConfirmation.GetDisplayName(_settings.PendingTheme)} theme. Choose the same combination to confirm it, or choose a different theme to start a new confirmation.";
        else
            StatusText =
                "Choose Light, Dark, or High Contrast. Reader and Theme are confirmed together across two launches to protect against an accidental first choice.";

        PrimaryButtonText = "_Save and continue";
        PrimaryButtonAutomationName = "Save Reader and Theme choices and continue";
        KeyboardHelpText =
            "Keyboard: use Up and Down Arrow to hear Light, Dark, and High Contrast. Tab moves to Back and Save and continue.";
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
        ReviewItems.Add(
            UseTikFinity
                ? "Streaming platform: TikTok through TikFinity; connect automatically when SafeSpeak opens"
                : "Streaming platform: TikFinity saved, automatic connection off");
        ReviewItems.Add(
            _settings.LocalConnectorAutoDetectConsent
                ? _settings.LocalConnectorDetectionStatus ==
                  OnboardingConnectorDetectionStatus.NotChecked
                    ? "Local connector detection: permission saved; no check was needed because automatic TikFinity connection is off."
                    : $"Local connector detection: {_settings.LocalConnectorDetectionSummary}"
                : "Local connector detection: not requested");
        ReviewItems.Add($"Language filtering: {ModelStatus}");
        ReviewItems.Add(
            "Safety startup: SafeSpeak opens disarmed and does not process chat until you arm it.");
    }

    private void ApplyDetectionResultToSettings(
        LocalConnectorDetectionResult? result)
    {
        if (!AutoDetectLocalConnectors || result is null)
        {
            _settings.LocalConnectorDetectionStatus =
                OnboardingConnectorDetectionStatus.NotChecked;
            _settings.LocalConnectorDetectionSummary =
                "Local connector detection was not requested.";
            return;
        }

        _settings.LocalConnectorDetectionStatus = result.Status switch
        {
            LocalConnectorDetectionStatus.Detected =>
                OnboardingConnectorDetectionStatus.Detected,
            LocalConnectorDetectionStatus.NotDetected =>
                OnboardingConnectorDetectionStatus.NotDetected,
            LocalConnectorDetectionStatus.TimedOut =>
                OnboardingConnectorDetectionStatus.TimedOut,
            LocalConnectorDetectionStatus.Failed =>
                OnboardingConnectorDetectionStatus.Failed,
            _ => OnboardingConnectorDetectionStatus.NotChecked
        };
        _settings.LocalConnectorDetectionSummary = result.SafeDescription;
    }

    private void RestorePersistedDetectionResult()
    {
        if (!_settings.LocalConnectorAutoDetectConsent ||
            _settings.LocalConnectorDetectionStatus ==
            OnboardingConnectorDetectionStatus.NotChecked)
        {
            return;
        }

        LocalConnectorDetectionStatus status =
            _settings.LocalConnectorDetectionStatus switch
            {
                OnboardingConnectorDetectionStatus.Detected =>
                    LocalConnectorDetectionStatus.Detected,
                OnboardingConnectorDetectionStatus.NotDetected =>
                    LocalConnectorDetectionStatus.NotDetected,
                OnboardingConnectorDetectionStatus.TimedOut =>
                    LocalConnectorDetectionStatus.TimedOut,
                _ => LocalConnectorDetectionStatus.Failed
            };
        DetectionResults.Add(new LocalConnectorDetectionResult(
            TikFinityWebSocketClient.ConnectorDescriptor.Id,
            TikFinityWebSocketClient.ConnectorDescriptor.DisplayName,
            status,
            _settings.LocalConnectorDetectionSummary));
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

    private void AnnounceCurrentPage() =>
        _announcer.Announce(
            $"{StepProgress}. {PromptText}. {StatusText} {KeyboardHelpText}",
            interrupt: true);

    private void RaisePageProperties()
    {
        OnPropertyChanged(nameof(IsReaderStep));
        OnPropertyChanged(nameof(IsThemeStep));
        OnPropertyChanged(nameof(IsPlatformStep));
        OnPropertyChanged(nameof(IsFilteringStep));
        OnPropertyChanged(nameof(IsReviewStep));
        OnPropertyChanged(nameof(IsRestartRequired));
        OnPropertyChanged(nameof(IsBackAvailable));
        OnPropertyChanged(nameof(IsPrimaryButtonVisible));
    }

    public void Dispose()
    {
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
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
        bool AutoConnectSource,
        bool LocalConnectorAutoDetectConsent,
        OnboardingConnectorDetectionStatus LocalConnectorDetectionStatus,
        string LocalConnectorDetectionSummary,
        bool AiClassificationEnabled)
    {
        public static AccessibilitySnapshot Capture(AppSettings settings) =>
            new(
                settings.SpokenGuidance,
                settings.Theme,
                settings.PendingSpokenGuidance,
                settings.PendingTheme,
                settings.OnboardingStage,
                settings.SelectedSourceConnectorId,
                settings.AutoConnectSource,
                settings.LocalConnectorAutoDetectConsent,
                settings.LocalConnectorDetectionStatus,
                settings.LocalConnectorDetectionSummary,
                settings.AiClassificationEnabled);

        public void Restore(AppSettings settings)
        {
            settings.SpokenGuidance = SpokenGuidance;
            settings.Theme = Theme;
            settings.PendingSpokenGuidance = PendingSpokenGuidance;
            settings.PendingTheme = PendingTheme;
            settings.OnboardingStage = OnboardingStage;
            settings.SelectedSourceConnectorId = SelectedSourceConnectorId;
            settings.AutoConnectSource = AutoConnectSource;
            settings.LocalConnectorAutoDetectConsent =
                LocalConnectorAutoDetectConsent;
            settings.LocalConnectorDetectionStatus =
                LocalConnectorDetectionStatus;
            settings.LocalConnectorDetectionSummary =
                LocalConnectorDetectionSummary;
            settings.AiClassificationEnabled = AiClassificationEnabled;
        }
    }
}
