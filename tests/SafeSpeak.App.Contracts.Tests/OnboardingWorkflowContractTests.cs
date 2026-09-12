using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SafeSpeak.App.Contracts.Tests;

public sealed class OnboardingWorkflowContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Wizard_ResumesPersistedStagesAndStartupStopsPromptingAfterCompletion()
    {
        string settings = Source("src", "SafeSpeak.Core", "Models", "AppSettings.cs");
        string wizard = WizardViewModel();
        string startup = Source("src", "SafeSpeak.App", "App.xaml.cs");

        Assert.Contains("Accessibility = 0", settings);
        Assert.Contains("Platform = 1", settings);
        Assert.Contains("Filtering = 2", settings);
        Assert.Contains("Review = 3", settings);
        Assert.Contains("Complete = 4", settings);
        Assert.Contains(
            "HasCompletedOnboarding => OnboardingStage == OnboardingStage.Complete",
            settings);

        string resolver = Method(wizard, "private AccessibilitySetupPage ResolveInitialPage()");
        Assert.Contains("OnboardingStage.Filtering => AccessibilitySetupPage.Filtering", resolver);
        Assert.Contains("OnboardingStage.Review => AccessibilitySetupPage.Review", resolver);
        Assert.Contains("_ => AccessibilitySetupPage.Platform", resolver);
        Assert.Contains("!settings.HasCompletedOnboarding ||", startup);
        Assert.Contains("settings.IsAwaitingAccessibilityConfirmation", startup);
    }

    [Fact]
    public void AccessibilityChoices_ContinueNowAndRequestConfirmationOnNextLaunch()
    {
        string confirmation = Source(
            "src", "SafeSpeak.Core", "Accessibility",
            "AccessibilityPreferencesConfirmation.cs");
        string wizard = WizardViewModel();
        XDocument xaml = LoadWizard();

        string select = Method(
            confirmation,
            "public static AccessibilityPreferencesSelectionResult Select(");
        Assert.Contains("StorePendingSelection(settings, spokenGuidance, theme)", select);
        Assert.Contains("AccessibilityPreferencesSelectionResult.ConfirmationPending", select);
        Assert.Contains("settings.PendingSpokenGuidance == spokenGuidance", select);
        Assert.Contains("settings.PendingTheme == theme", select);
        Assert.Contains("settings.OnboardingStage = OnboardingStage.Platform", select);
        Assert.Contains("AccessibilityPreferencesSelectionResult.ChangedConfirmationPending", select);

        Assert.Contains("confirmation 2 of 2", wizard);
        Assert.Contains("Choose the same combination to confirm it", wizard);
        Assert.Contains("asks you to confirm them the next time it launches", wizard);
        Assert.Contains("Continuing setup now", wizard);
        Assert.Contains("_confirmationOnly", wizard);
        Assert.Contains("_settings.OnboardingStage = OnboardingStage.Complete", wizard);
        Assert.Contains("_onCompleted()", wizard);
        Assert.DoesNotContain("Close SafeSpeak", wizard);
        Assert.DoesNotContain("RestartRequired", wizard);
        Assert.DoesNotContain(
            xaml.Descendants(Presentation + "TextBlock"),
            element =>
                (element.Attribute("Text")?.Value ?? string.Empty)
                .Contains("remain closed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConnectorSetup_UsesExplicitTikFinityOrTikTokDirectChoicesWithoutAutoDetection()
    {
        XDocument xaml = LoadWizard();
        string wizard = WizardViewModel();

        string[] connectorNames = xaml.Descendants(Presentation + "Button")
            .Select(element => Attribute(element, "AutomationProperties.Name"))
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
        Assert.Contains("Use TikFinity for TikTok chat", connectorNames);
        Assert.Contains("Use TikTok Direct by username", connectorNames);
        Assert.DoesNotContain(connectorNames, name =>
            name.Contains("detect", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("AutoDetectLocalConnectors", wizard);
        Assert.DoesNotContain("DetectionResults", wizard);
        Assert.DoesNotContain("LocalConnectorDetector", wizard);
        Assert.DoesNotContain("DetectAsync", wizard);
    }

    [Fact]
    public void Filtering_ReportsBundledModelOrDeterministicFallbackWithoutFakeAcquisition()
    {
        XDocument xaml = LoadWizard();
        string wizard = WizardViewModel();
        string modelCheck = Method(wizard, "private async Task RefreshModelStatusAsync()");

        XElement filteringCopy = xaml.Descendants(Presentation + "TextBlock")
            .Single(element =>
                (element.Attribute("Text")?.Value ?? string.Empty)
                .StartsWith("The bundled model complements", StringComparison.Ordinal));
        Assert.Contains("No model download or cloud account is required", filteringCopy.Attribute("Text")!.Value);

        Assert.Contains("new LocalOnnxIntentClassifier()", modelCheck);
        Assert.Contains("classifier.IsModelLoaded", modelCheck);
        Assert.Contains("classifier.AvailabilityMessage", modelCheck);
        Assert.Contains("Enhanced filtering is installed and active", modelCheck);
        Assert.Contains("Deterministic filtering and banned terms remain active", modelCheck);
        Assert.DoesNotContain("LocalIntentModelManager", modelCheck);
        Assert.DoesNotContain("GooglePerspectiveClassifier", modelCheck);
        Assert.DoesNotContain("HttpClient", modelCheck);
        Assert.DoesNotContain("download complete", modelCheck, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("downloaded", modelCheck, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Review_ExposesEveryChoiceAndCompletesOnlyAfterSuccessfulSave()
    {
        string wizard = WizardViewModel();
        string review = Method(wizard, "private void BuildReviewItems()");
        string complete = Method(wizard, "private void CompleteOnboarding()");
        XDocument xaml = LoadWizard();

        Assert.Contains("Built-in spoken guidance:", review);
        Assert.Contains("Visual theme:", review);
        Assert.Contains("Configured connectors:", review);
        Assert.Contains("Language filtering:", review);
        Assert.Contains("SafeSpeak opens disarmed", review);

        XElement reviewList = xaml.Descendants(Presentation + "ListBox")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "ReviewList");
        Assert.Equal("{Binding ReviewItems}", reviewList.Attribute("ItemsSource")?.Value);
        Assert.Equal("Setup review choices", Attribute(reviewList, "AutomationProperties.Name"));
        Assert.Contains("Up and Down Arrow", Attribute(reviewList, "AutomationProperties.HelpText"));

        int markComplete = complete.IndexOf(
            "_settings.OnboardingStage = OnboardingStage.Complete",
            StringComparison.Ordinal);
        int save = complete.IndexOf("_settings.TrySave", StringComparison.Ordinal);
        int callback = complete.IndexOf("_onCompleted()", StringComparison.Ordinal);
        Assert.True(markComplete >= 0 && save > markComplete && callback > save);
        Assert.Contains("remains disarmed until you choose Arm SafeSpeak", complete);
    }

    [Fact]
    public void RunSetupAgain_ReusesSettingsAndResetOnboardingDoesNotEraseUnrelatedData()
    {
        string main = Source("src", "SafeSpeak.App", "ViewModels", "MainViewModel.cs");
        string settings = Source("src", "SafeSpeak.Core", "Models", "AppSettings.cs");
        string rerun = Method(main, "public void RerunAccessibilityWizard()");
        string reset = Method(settings, "public void ResetOnboarding()");

        Assert.Contains("new AccessibilitySetupViewModel(", rerun);
        Assert.Contains("_settings,", rerun);
        Assert.Contains("changeExistingProfile: true", rerun);
        Assert.DoesNotContain("ResetOnboarding", rerun);
        Assert.DoesNotContain("new AppSettings", rerun);

        string[] assignedProperties = Regex.Matches(
                reset,
                @"^\s*(\w+)\s*=",
                RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "OnboardingStage",
                "SpokenGuidance",
                "Theme",
                "PendingSpokenGuidance",
                "PendingTheme"
            },
            assignedProperties);
        Assert.DoesNotContain("CustomBlockedTerms", reset);
        Assert.DoesNotContain("SelectedVoiceName", reset);
        Assert.DoesNotContain("EnableStreamAuditLogging", reset);
        Assert.DoesNotContain("SpeechRate", reset);
        Assert.DoesNotContain("SpeechVolume", reset);
    }

    [Fact]
    public void CancellingRunSetupAgainRestoresItsPersistedStartingState()
    {
        string wizard = WizardViewModel();
        string constructor = Method(
            wizard,
            "public AccessibilitySetupViewModel(");
        string dispose = Method(wizard, "public void Dispose()");

        Assert.Contains(
            "_settingsRerunSnapshot = changeExistingProfile",
            constructor,
            StringComparison.Ordinal);
        Assert.Contains(
            "AccessibilitySnapshot.Capture(settings)",
            constructor,
            StringComparison.Ordinal);
        Assert.Contains(
            "_settingsRerunSnapshot is { } snapshot",
            dispose,
            StringComparison.Ordinal);
        Assert.Contains("snapshot.Restore(_settings)", dispose, StringComparison.Ordinal);
        Assert.Contains("_settings.TrySave(out _)", dispose, StringComparison.Ordinal);

        string xaml = Source("src", "SafeSpeak.App", "MainWindow.xaml");
        Assert.Contains(
            "Cancel preserves your current settings, logs, imported voices, and downloaded models.",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EachForwardStepPersistsItsResumeStageBeforeNavigation()
    {
        string wizard = WizardViewModel();
        AssertPersistsBeforeNavigation(
            Method(wizard, "private void CompletePlatformStep()"),
            "_settings.OnboardingStage = OnboardingStage.Voice");
        AssertPersistsBeforeNavigation(
            Method(wizard, "private void CompleteVoiceStep()"),
            "_settings.OnboardingStage = OnboardingStage.Filtering");
        AssertPersistsBeforeNavigation(
            Method(wizard, "private void CompleteFilteringStep()"),
            "_settings.OnboardingStage = OnboardingStage.Keybinds");
        AssertPersistsBeforeNavigation(
            Method(wizard, "private void CompleteKeybindsStep()"),
            "_settings.OnboardingStage = OnboardingStage.Navigation");
        AssertPersistsBeforeNavigation(
            Method(wizard, "private void CompleteNavigationStep()"),
            "_settings.OnboardingStage = OnboardingStage.Review");
        AssertPersistsBeforeNavigation(
            Method(wizard, "private void CompleteOnboarding()"),
            "_settings.OnboardingStage = OnboardingStage.Complete",
            navigationMarker: "_onCompleted()");
    }

    [Fact]
    public void SetupRerun_DoesNotAutoOpenConnectorModalUntilUserInteraction()
    {
        string wizard = WizardViewModel();
        string constructor = Method(wizard, "public AccessibilitySetupViewModel(");
        string onTikTokChanged = Method(wizard, "partial void OnUseTikTokDirectChanged(bool value)");
        string restart = Method(wizard, "public void RestartSetup()");

        // State initialization order in constructor: TikTokUsername must be assigned before UseTikTokDirect
        int usernameIndex = constructor.IndexOf("TikTokUsername = _settings.TikTokUsername;", StringComparison.Ordinal);
        int useTikTokIndex = constructor.IndexOf("UseTikTokDirect = _settings.ConfiguredSourceConnectorIds.Contains(", StringComparison.Ordinal);
        Assert.True(usernameIndex >= 0 && useTikTokIndex > usernameIndex, "TikTokUsername must be initialized before UseTikTokDirect");

        // ActiveModalConnectorId must be guaranteed null before initialization ends
        Assert.Contains("ActiveModalConnectorId = null;", constructor);

        // OnUseTikTokDirectChanged must guard modal launch so it never auto-triggers during initialization or non-platform steps
        Assert.Contains("_initialized", onTikTokChanged);
        Assert.Contains("CurrentPage == AccessibilitySetupPage.Platform", onTikTokChanged);
        Assert.Contains("string.IsNullOrWhiteSpace(TikTokUsername)", onTikTokChanged);

        // RestartSetup must cleanly reset modal state
        Assert.Contains("ActiveModalConnectorId = null;", restart);
    }

    [Fact]
    public void Startup_PromptsExistingUsersOnSetupUpdateWithAccessibleYesAndDeclineActions()
    {
        string startup = Source("src", "SafeSpeak.App", "App.xaml.cs");
        string dialogXaml = Source("src", "SafeSpeak.App", "Views", "SetupUpdatePromptDialog.xaml");
        string dialogCode = Source("src", "SafeSpeak.App", "Views", "SetupUpdatePromptDialog.xaml.cs");

        // App.xaml.cs handles ShouldPromptSetupUpdate
        Assert.Contains("else if (settings.ShouldPromptSetupUpdate)", startup);
        Assert.Contains("new SetupUpdatePromptDialog(", startup);
        Assert.Contains("changeExistingProfile: true", startup);

        // Dialog markup defines Yes and Decline buttons with accessible hotkeys and minimum hit targets
        XDocument xaml = XDocument.Parse(dialogXaml);
        XElement yesButton = xaml.Descendants(Presentation + "Button")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "YesButton");
        XElement declineButton = xaml.Descendants(Presentation + "Button")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "DeclineButton");

        Assert.Equal("1", yesButton.Attribute("TabIndex")?.Value);
        Assert.Equal("2", declineButton.Attribute("TabIndex")?.Value);
        Assert.True(double.Parse(yesButton.Attribute("MinHeight")!.Value) >= 44);
        Assert.True(double.Parse(declineButton.Attribute("MinHeight")!.Value) >= 44);
        Assert.Contains("(Y)", yesButton.Attribute("Content")!.Value);
        Assert.Contains("(N)", declineButton.Attribute("Content")!.Value);

        // Codebehind handles keyboard Y, N, Enter, Escape, and saves LastAcknowledgedSetupVersion
        Assert.Contains("e.Key == Key.Y", dialogCode);
        Assert.Contains("e.Key is Key.N or Key.Escape", dialogCode);
        Assert.Contains("_settings.LastAcknowledgedSetupVersion = AppSettings.CurrentSetupGuideVersion;", dialogCode);
        Assert.Contains("_settings.TrySave(out _);", dialogCode);
    }

    [Fact]
    public void IncompleteOnboarding_StartsOverCleanlyAndStartupResetsUncommittedChanges()
    {
        string startup = Source("src", "SafeSpeak.App", "App.xaml.cs");
        string wizard = WizardViewModel();

        // Startup checks for incomplete onboarding and resets uncommitted state
        Assert.Contains("settings.ResetIncompleteOnboarding();", startup);

        // Wizard initializes snapshot for uncompleted runs and forces initialPage to Reader
        string constructor = Method(wizard, "public AccessibilitySetupViewModel(");
        Assert.Contains("_initialOnboardingSnapshot = !settings.HasCompletedOnboarding", constructor);
        Assert.Contains("!_settings.HasCompletedOnboarding", constructor);
        Assert.Contains("? AccessibilitySetupPage.Reader", constructor);

        // ResolveInitialPage guards uncompleted onboarding from resuming midway
        string resolver = Method(wizard, "private AccessibilitySetupPage ResolveInitialPage()");
        Assert.Contains("!_settings.HasCompletedOnboarding", resolver);

        // Dispose reverts uncompleted onboarding session to clean state when closed without completion
        string dispose = Method(wizard, "public void Dispose()");
        Assert.Contains("else if (!_settings.HasCompletedOnboarding)", dispose);
        Assert.Contains("initialSnapshot.Restore(_settings);", dispose);
        Assert.Contains("_settings.OnboardingStage = OnboardingStage.Accessibility;", dispose);
    }

    [Fact]
    public void Step4_TestVoiceAndKokoroInstallDetection_WiredCorrectly()
    {
        XDocument xaml = LoadWizard();
        string wizard = WizardViewModel();

        // PreviewVoiceButton must bind to TestVoiceCommand and CanTestVoice
        XElement previewButton = xaml.Descendants(Presentation + "Button")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "PreviewVoiceButton");
        Assert.Equal("{Binding TestVoiceCommand}", previewButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding CanTestVoice}", previewButton.Attribute("IsEnabled")?.Value);

        // KokoroInstallButton must bind to InstallKokoroCommand, CanInstallKokoro, and dynamic text
        XElement kokoroButton = xaml.Descendants(Presentation + "Button")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "KokoroInstallButton");
        Assert.Equal("{Binding InstallKokoroCommand}", kokoroButton.Attribute("Command")?.Value);
        Assert.Equal("{Binding CanInstallKokoro}", kokoroButton.Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding KokoroInstallButtonText}", kokoroButton.Attribute("Content")?.Value);

        // ViewModel exposes command alias and wires VoiceId on preview output
        Assert.Contains("public IRelayCommand PreviewVoiceCommand => TestVoiceCommand;", wizard);
        Assert.Contains("public bool CanTestVoice =>", wizard);
        Assert.Contains("public bool CanInstallKokoro => !IsKokoroInstalled", wizard);
        Assert.Contains("public string KokoroInstallButtonText => IsKokoroInstalled", wizard);
        Assert.Contains("public string KokoroInstallStatus => IsKokoroInstalled", wizard);

        string testVoice = Method(wizard, "public async Task TestVoiceAsync()");
        Assert.Contains("_previewOutput.VoiceId = SelectedVoice;", testVoice);
        Assert.Contains("await _previewOutput.SpeakAsync(", testVoice);
    }

    [Fact]
    public void Wizard_NavigationStepHeaderDoesNotDuplicateBodyViewList()
    {
        string wizard = WizardViewModel();
        string configureNavigation = Method(wizard, "private void ConfigureNavigationPage()");

        // StatusText in the header must provide a concise overview rather than re-listing the 4 views and hotkeys
        Assert.DoesNotContain("Live chat (Ctrl+1)", configureNavigation);
        Assert.DoesNotContain("Safety filtering (Ctrl+2)", configureNavigation);
        Assert.DoesNotContain("Voice selection (Ctrl+3)", configureNavigation);
        Assert.DoesNotContain("Settings (Ctrl+4)", configureNavigation);
        Assert.Contains("Overview of SafeSpeak's primary views and navigation shortcuts", configureNavigation);
    }

    private static void AssertPersistsBeforeNavigation(
        string method,
        string stageAssignment,
        string navigationMarker = "NavigateTo(")
    {
        int stage = method.IndexOf(stageAssignment, StringComparison.Ordinal);
        int save = method.IndexOf("_settings.TrySave", StringComparison.Ordinal);
        int navigate = method.IndexOf(navigationMarker, StringComparison.Ordinal);
        Assert.True(stage >= 0 && save > stage && navigate > save);
    }

    private static XDocument LoadWizard() =>
        XDocument.Load(
            RepositoryFile(
                "src", "SafeSpeak.App", "Views",
                "AccessibilitySetupDialog.xaml"));

    private static string WizardViewModel() =>
        Source(
            "src", "SafeSpeak.App", "ViewModels",
            "AccessibilitySetupViewModel.cs");

    private static string Source(params string[] segments) =>
        File.ReadAllText(RepositoryFile(segments));

    private static string Method(string source, string signature)
    {
        int signatureStart = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureStart >= 0, $"Method signature not found: {signature}");
        int bodyStart = source.IndexOf('{', signatureStart);
        Assert.True(bodyStart >= 0, $"Method body not found: {signature}");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] != '}') continue;
            depth--;
            if (depth == 0) return source[signatureStart..(index + 1)];
        }

        throw new InvalidOperationException($"Unterminated method body: {signature}");
    }

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == localName)
            ?.Value;

    private static string RepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SafeSpeak.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            new[] { directory!.FullName }.Concat(segments).ToArray());
    }
}
