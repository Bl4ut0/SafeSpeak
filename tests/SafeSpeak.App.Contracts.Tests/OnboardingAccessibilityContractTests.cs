using System.Xml.Linq;

namespace SafeSpeak.App.Contracts.Tests;

public sealed class OnboardingAccessibilityContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Wizard_UsesNativeKeyboardControlsWithPageLocalTabOrder()
    {
        XDocument document = LoadWizard();
        XElement root = document.Root!;
        Assert.Equal("Cycle", Attribute(root, "KeyboardNavigation.TabNavigation"));

        XElement[] persistentControls = document
            .Descendants()
            .Where(element =>
                element.Attribute("TabIndex") is not null &&
                !element.Ancestors(Presentation + "StackPanel").Any(ancestor =>
                    (ancestor.Attribute("Visibility")?.Value ?? string.Empty)
                    .Contains("Step", StringComparison.Ordinal)))
            .ToArray();
        int[] persistentIndexes = persistentControls
            .Select(element => int.Parse(element.Attribute("TabIndex")!.Value))
            .Order()
            .ToArray();

        Assert.Equal(new[] { 90, 91 }, persistentIndexes);
        Assert.Equal(persistentIndexes.Length, persistentIndexes.Distinct().Count());

        XElement[] stepPanels = document
            .Descendants(Presentation + "StackPanel")
            .Where(element =>
                (element.Attribute("Visibility")?.Value ?? string.Empty)
                .Contains("Step", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(5, stepPanels.Length);
        Assert.All(stepPanels, panel =>
        {
            Assert.Equal("Local", Attribute(panel, "KeyboardNavigation.TabNavigation"));
            int[] indexes = panel
                .Descendants()
                .Where(element => element.Attribute("TabIndex") is not null)
                .Select(element => int.Parse(element.Attribute("TabIndex")!.Value))
                .Order()
                .ToArray();
            Assert.Equal(indexes.Length, indexes.Distinct().Count());
            Assert.Equal(Enumerable.Range(1, indexes.Length), indexes);
        });

        Assert.All(
            document.Descendants()
                .Where(element => element.Attribute("TabIndex") is not null),
            element => Assert.Contains(
                element.Name.LocalName,
                new[] { "Button", "CheckBox", "ListBox" }));
    }

    [Fact]
    public void Wizard_OpensWithDirectYesNoReaderQuestionThenTheme()
    {
        XDocument document = LoadWizard();
        string viewModel = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "ViewModels",
                "AccessibilitySetupViewModel.cs"));
        string codeBehind = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "Views",
                "AccessibilitySetupDialog.xaml.cs"));

        XElement yes = document.Descendants(Presentation + "Button")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "ReaderYesButton");
        XElement no = document.Descendants(Presentation + "Button")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "ReaderNoButton");

        Assert.Equal("{Binding ChooseSpokenGuidanceYesCommand}", yes.Attribute("Command")?.Value);
        Assert.Equal("{Binding ChooseSpokenGuidanceNoCommand}", no.Attribute("Command")?.Value);
        Assert.Contains("_Yes", yes.Attribute("Content")?.Value);
        Assert.Contains("(Y)", yes.Attribute("Content")?.Value);
        Assert.Contains("_No", no.Attribute("Content")?.Value);
        Assert.Contains("(N)", no.Attribute("Content")?.Value);
        Assert.Contains("Do you want to use the SafeSpeak built-in screen reader?", viewModel);
        Assert.Contains("AccessibilitySetupPage.Reader", viewModel);
        Assert.Contains("AccessibilitySetupPage.Theme", viewModel);
        Assert.Contains("CompleteReaderStep(enabled: true)", viewModel);
        Assert.Contains("CompleteReaderStep(enabled: false)", viewModel);
        Assert.Contains("_announcer.IsEnhancedAccessibilityEnabled = enabled", viewModel);
        Assert.Contains("NavigateTo(AccessibilitySetupPage.Theme)", viewModel);
        Assert.Contains("Step 1 of 5", viewModel);
        Assert.Contains("Step 2 of 5", viewModel);
        Assert.Contains("e.Key == Key.Y", codeBehind);
        Assert.Contains("e.Key == Key.N", codeBehind);
        Assert.DoesNotContain("GuidanceCheckBox", codeBehind);
    }

    [Fact]
    public void Wizard_EveryFocusableControlHasAnAccessibleName()
    {
        XDocument document = LoadWizard();
        XElement[] focusableControls = document
            .Descendants()
            .Where(element =>
                element.Attribute("TabIndex") is not null ||
                element.Name == Presentation + "ProgressBar")
            .ToArray();

        Assert.NotEmpty(focusableControls);
        Assert.All(
            focusableControls,
            element =>
            {
                string? automationName =
                    Attribute(element, "AutomationProperties.Name");
                Assert.False(
                    string.IsNullOrWhiteSpace(automationName),
                    $"{element.Name.LocalName} is missing AutomationProperties.Name.");
            });
    }

    [Fact]
    public void Wizard_ExposesProgressStatusAndModelAsPoliteLiveRegions()
    {
        XDocument document = LoadWizard();
        string[] liveBindings = document
            .Descendants()
            .Where(element =>
                Attribute(element, "AutomationProperties.LiveSetting") == "Polite")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        Assert.Contains("{Binding StepProgress}", liveBindings);
        Assert.Contains("{Binding StatusText}", liveBindings);
        Assert.Contains("{Binding ModelStatus}", liveBindings);
    }

    [Fact]
    public void Wizard_ThemeListUsesArrowNavigationAndExactNamedOptions()
    {
        XDocument document = LoadWizard();
        XElement themeList = document
            .Descendants(Presentation + "ListBox")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "ThemeList");
        string help =
            Attribute(themeList, "AutomationProperties.HelpText")
            ?? string.Empty;
        string viewModel = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "ViewModels",
                "AccessibilitySetupViewModel.cs"));

        Assert.Contains("Up and Down Arrow", help);
        Assert.Contains("\"Light theme, option 1 of 3\"", viewModel);
        Assert.Contains("\"Dark theme, option 2 of 3\"", viewModel);
        Assert.Contains("\"High Contrast theme, option 3 of 3\"", viewModel);
        Assert.DoesNotContain("Default theme", viewModel);
    }

    [Fact]
    public void Wizard_PrimaryTargetsMeetMinimumSizeAndFocusIsStepSpecific()
    {
        XDocument document = LoadWizard();
        XElement[] primaryTargets = document
            .Descendants()
            .Where(element => element.Attribute("TabIndex") is not null)
            .ToArray();
        string codeBehind = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "Views",
                "AccessibilitySetupDialog.xaml.cs"));

        Assert.All(
            primaryTargets,
            element =>
            {
                string? heightText = element.Attribute("MinHeight")?.Value;
                if (element.Name == Presentation + "ListBox")
                {
                    Assert.NotNull(heightText);
                }
                else
                {
                    Assert.True(
                        double.TryParse(heightText, out double height) &&
                        height >= 44,
                        $"{element.Name.LocalName} target is smaller than 44 DIPs.");
                }
            });
        Assert.Contains(
            "AccessibilitySetupPage.Reader => ReaderYesButton",
            codeBehind);
        Assert.Contains(
            "AccessibilitySetupPage.Theme => ThemeList",
            codeBehind);
        Assert.Contains(
            "AccessibilitySetupPage.Platform => TikFinityCheckBox",
            codeBehind);
        Assert.Contains(
            "AccessibilitySetupPage.Review => ReviewList",
            codeBehind);
        Assert.Contains("_ => PrimaryButton", codeBehind);
    }

    [Fact]
    public void Wizard_SaveFailureProducesOneInterruptingAnnouncement()
    {
        string viewModel = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "ViewModels",
                "AccessibilitySetupViewModel.cs"));
        int methodStart = viewModel.IndexOf(
            "private void ReportSaveFailure",
            StringComparison.Ordinal);
        int nextMethod = viewModel.IndexOf(
            "private void AnnounceCurrentPage",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && nextMethod > methodStart);

        string method = viewModel[methodStart..nextMethod];
        Assert.Equal(1, Count(method, "_announcer.Announce("));
        Assert.Contains("interrupt: true", method);
        Assert.Contains("FocusRequested?.Invoke", method);
    }

    private static XDocument LoadWizard() =>
        XDocument.Load(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "Views",
                "AccessibilitySetupDialog.xaml"),
            LoadOptions.SetLineInfo);

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName == localName)
            ?.Value;

    private static int Count(string text, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string RepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory =
            new(AppContext.BaseDirectory);
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
