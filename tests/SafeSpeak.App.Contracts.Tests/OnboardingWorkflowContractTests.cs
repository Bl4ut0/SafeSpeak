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
        Assert.Contains("if (!settings.HasCompletedOnboarding)", startup);
    }

    [Fact]
    public void AccessibilityChoices_RequireMatchingSecondLaunchAndExplainCloseReopenFlow()
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
        Assert.Contains("AccessibilityPreferencesSelectionResult.RestartRequired", select);
        Assert.Contains("settings.PendingSpokenGuidance == spokenGuidance", select);
        Assert.Contains("settings.PendingTheme == theme", select);
        Assert.Contains("settings.OnboardingStage = OnboardingStage.Platform", select);
        Assert.Contains("AccessibilityPreferencesSelectionResult.ChangedRestartRequired", select);

        Assert.Contains("confirmation 2 of 2", wizard);
        Assert.Contains("Choose the same combination to confirm it", wizard);
        Assert.Contains("_Close SafeSpeak", wizard);
        Assert.Contains("Close SafeSpeak, reopen it", wizard);
        Assert.Contains("A different combination becomes a new first choice", wizard);

        XElement restartMessage = xaml.Descendants(Presentation + "TextBlock")
            .Single(element =>
                (element.Attribute("Text")?.Value ?? string.Empty)
                .StartsWith("SafeSpeak will remain closed", StringComparison.Ordinal));
        Assert.Contains("second confirmation", restartMessage.Attribute("Text")!.Value);
        Assert.Contains(
            "Restart required",
            Attribute(restartMessage, "AutomationProperties.Name"));
    }

    [Fact]
    public void ConnectorDetection_IsExplicitlyConsentedAndLimitedToApprovedLocalSignals()
    {
        XDocument xaml = LoadWizard();
        string wizard = WizardViewModel();
        string detector = Source(
            "src", "SafeSpeak.Core", "Connectors", "LocalConnectorDetector.cs");

        XElement consent = xaml.Descendants(Presentation + "CheckBox")
            .Single(element =>
                Attribute(element, "AutomationProperties.Name") ==
                "Allow local TikFinity auto-detection, recommended");
        Assert.Equal("{Binding AutoDetectLocalConnectors}", consent.Attribute("IsChecked")?.Value);
        string help = Attribute(consent, "AutomationProperties.HelpText") ?? string.Empty;
        Assert.Contains("explicit consent", help);
        Assert.Contains("approved TikFinity process names", help);
        Assert.Contains("local port 21213", help);
        Assert.Contains("does not connect, authenticate, arm SafeSpeak, or scan files", help);

        string platform = Method(wizard, "private async Task CompletePlatformStepAsync()");
        int consentGuard = platform.IndexOf(
            "if (UseTikFinity && AutoDetectLocalConnectors)",
            StringComparison.Ordinal);
        int detectionCall = platform.IndexOf("_connectorDetector.DetectAsync(", StringComparison.Ordinal);
        Assert.True(consentGuard >= 0 && detectionCall > consentGuard);
        Assert.Contains("userConsented: true", platform);

        int refusal = detector.IndexOf("if (!userConsented)", StringComparison.Ordinal);
        int firstProbe = detector.IndexOf(
            "_probe.IsAnyApprovedProcessRunningAsync",
            StringComparison.Ordinal);
        Assert.True(refusal >= 0 && firstProbe > refusal);
        Assert.Contains("[\"TikFinity\", \"TikFinityApp\"]", detector);
        Assert.Contains("[21213]", detector);
        Assert.Contains("TimeSpan.FromSeconds(1)", detector);
        Assert.DoesNotContain("HttpClient", detector);
        Assert.DoesNotContain("TcpClient", detector);
        Assert.DoesNotContain("ConnectAsync", detector);
        Assert.DoesNotContain("File.", detector);
        Assert.DoesNotContain("Directory.", detector);
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
        Assert.Contains("Streaming platform:", review);
        Assert.Contains("Local connector detection:", review);
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
            Method(wizard, "private async Task CompletePlatformStepAsync()"),
            "_settings.OnboardingStage = OnboardingStage.Filtering");
        AssertPersistsBeforeNavigation(
            Method(wizard, "private void CompleteFilteringStep()"),
            "_settings.OnboardingStage = OnboardingStage.Review");
        AssertPersistsBeforeNavigation(
            Method(wizard, "private void CompleteOnboarding()"),
            "_settings.OnboardingStage = OnboardingStage.Complete",
            navigationMarker: "_onCompleted()");
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
