using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SafeSpeak.App.Contracts.Tests;

public sealed class MainShellAccessibilityContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly HashSet<string> NativeFocusableControls =
        new(StringComparer.Ordinal)
        {
            "Button",
            "CheckBox",
            "ComboBox",
            "ListBox",
            "ListView",
            "Slider",
            "TabControl",
            "TextBox",
            "ToggleButton"
        };

    [Fact]
    public void MainWindow_UsesNativeFocusableControlsInUniqueLogicalTabOrder()
    {
        XDocument document = LoadMainWindow();
        XElement root = document.Root!;
        XElement[] tabStops = TabStops(document);

        Assert.Equal("Cycle", Attribute(root, "KeyboardNavigation.TabNavigation"));
        Assert.NotEmpty(tabStops);
        Assert.All(
            tabStops,
            element => Assert.Contains(
                element.Name.LocalName,
                NativeFocusableControls));

        XElement[] persistentTabStops = tabStops
            .Where(element => !element.Ancestors(Presentation + "TabItem").Any())
            .ToArray();
        int[] persistentIndexes = persistentTabStops
            .Select(element => int.Parse(element.Attribute("TabIndex")!.Value))
            .ToArray();

        Assert.Equal(
            persistentIndexes.Length,
            persistentIndexes.Distinct().Count());
        Assert.Equal(new[] { 1, 2 }, persistentIndexes.Order());

        foreach (XElement tab in document.Descendants(Presentation + "TabItem"))
        {
            XElement[] pageTabStops = tab
                .Descendants()
                .Where(element => element.Attribute("TabIndex") is not null)
                .ToArray();
            int[] pageIndexes = pageTabStops
                .Select(element => int.Parse(element.Attribute("TabIndex")!.Value))
                .ToArray();

            Assert.NotEmpty(pageTabStops);
            Assert.Equal(pageIndexes.Length, pageIndexes.Distinct().Count());
            Assert.Equal(Enumerable.Range(1, pageIndexes.Length), pageIndexes.Order());
            Assert.Contains(tab.Descendants(), element =>
                Attribute(element, "KeyboardNavigation.TabNavigation") == "Local");
        }
    }

    [Fact]
    public void MainWindow_FocusableControlsHaveAccessibleNames()
    {
        XDocument document = LoadMainWindow();
        XElement[] tabStops = TabStops(document);
        string[] missingNames = tabStops
            .Where(element => !HasAccessibleName(document, element))
            .Select(Describe)
            .ToArray();

        Assert.True(
            missingNames.Length == 0,
            "Focusable controls without an accessible name or targeting label: " +
            string.Join(", ", missingNames));
    }

    [Fact]
    public void MainWindow_PrimaryActionsSeparateNamesFromConsequenceHelp()
    {
        XDocument document = LoadMainWindow();
        XElement liveTab = document
            .Descendants(Presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "_Live");
        XElement[] primaryActions = TabStops(document)
            .Where(element => !element.Ancestors(Presentation + "TabItem").Any())
            .Concat(liveTab.Descendants().Where(element =>
                element.Attribute("TabIndex") is not null &&
                int.Parse(element.Attribute("TabIndex")!.Value) <= 8))
            .ToArray();
        string[] primaryActionsWithoutHelp = primaryActions
            .Where(element =>
                string.IsNullOrWhiteSpace(
                    Attribute(element, "AutomationProperties.HelpText")))
            .Select(Describe)
            .ToArray();

        Assert.True(
            primaryActionsWithoutHelp.Length == 0,
            "Primary actions must keep the short accessible name separate from " +
            "keyboard/consequence HelpText. Missing HelpText: " +
            string.Join(", ", primaryActionsWithoutHelp));
    }

    [Fact]
    public void MainWindow_SelectorsAndSlidersExplainArrowKeyOperation()
    {
        XElement[] selectorsAndSliders = TabStops(LoadMainWindow())
            .Where(element =>
                element.Name.LocalName is "ComboBox" or "Slider" or "TabControl")
            .ToArray();
        string[] selectorsWithoutHelp = selectorsAndSliders
            .Where(element =>
                string.IsNullOrWhiteSpace(
                    Attribute(element, "AutomationProperties.HelpText")))
            .Select(Describe)
            .ToArray();

        Assert.True(
            selectorsWithoutHelp.Length == 0,
            "Selectors, sliders, and tab navigation must explain their arrow-key " +
            "operation in HelpText. Missing HelpText: " +
            string.Join(", ", selectorsWithoutHelp));
    }

    [Fact]
    public void MainWindow_HasNoFocusableControlsInsideHiddenSecondarySurfaces()
    {
        XDocument document = LoadMainWindow();
        string[] hiddenTabStops = TabStops(document)
            .Where(element => element
                .AncestorsAndSelf()
                .Any(ancestor =>
                    Attribute(ancestor, "Visibility") is "Collapsed" or "Hidden"))
            .Select(Describe)
            .ToArray();

        Assert.True(
            hiddenTabStops.Length == 0,
            "Hidden or secondary surfaces must not retain TabIndex focus targets: " +
            string.Join(", ", hiddenTabStops));
    }

    [Theory]
    [InlineData("OutputCombo", "AudioEndpoints", "Name")]
    [InlineData("VoiceCombo", "Voices", "DisplayName")]
    public void ObjectBackedAudioAndVoiceOptionsBindVisibleAndContainerNames(
        string comboName,
        string itemsSource,
        string itemNameProperty)
    {
        XDocument document = LoadMainWindow();
        XElement combo = NamedElement(document, "ComboBox", comboName);
        string expectedItemsSource = $"{{Binding {itemsSource}}}";
        string expectedItemName = $"{{Binding {itemNameProperty}}}";

        Assert.Equal(expectedItemsSource, combo.Attribute("ItemsSource")?.Value);

        XElement templateText = combo
            .Descendants(Presentation + "DataTemplate")
            .Descendants()
            .Single(element => element.Attribute("Text") is not null);
        Assert.Equal(expectedItemName, templateText.Attribute("Text")?.Value);
        Assert.Equal(
            expectedItemName,
            Attribute(templateText, "AutomationProperties.Name"));

        XElement? itemContainerNameSetter = combo
            .Descendants(Presentation + "Setter")
            .SingleOrDefault(element =>
                element.Attribute("Property")?.Value ==
                "AutomationProperties.Name");

        Assert.True(
            itemContainerNameSetter is not null,
            $"{comboName} at {Describe(combo)} must bind " +
            $"AutomationProperties.Name on each ComboBoxItem container.");
        Assert.Equal(
            expectedItemName,
            itemContainerNameSetter!.Attribute("Value")?.Value);
    }

    [Fact]
    public void BroadcastAudioOptionsUseAudioEndpointInfoNameNotDisplayName()
    {
        XDocument document = LoadMainWindow();
        XElement combo = NamedElement(document, "ComboBox", "OutputCombo");
        string interfaceSource = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.Core",
                "Audio",
                "IAudioRouter.cs"));

        Assert.Matches(
            @"AudioEndpointInfo\s*\(\s*string\s+Id\s*,\s*string\s+Name\b",
            interfaceSource);
        Assert.All(
            combo.DescendantsAndSelf()
                .Attributes()
                .Where(attribute =>
                    attribute.Value.Contains("Binding", StringComparison.Ordinal)),
            attribute => Assert.DoesNotContain(
                "DisplayName",
                attribute.Value,
                StringComparison.Ordinal));
        Assert.Contains(
            combo.DescendantsAndSelf().Attributes(),
            attribute => attribute.Value == "{Binding Name}");
    }

    [Fact]
    public void EveryMainWindowTabStopResolvesToTheThreeDipFocusVisual()
    {
        XDocument mainWindow = LoadMainWindow();
        XDocument appResources = XDocument.Load(
            RepositoryFile("src", "SafeSpeak.App", "App.xaml"),
            LoadOptions.SetLineInfo);
        XElement focusStyle = appResources
            .Descendants(Presentation + "Style")
            .Single(element =>
                element.Attribute(Xaml + "Key")?.Value ==
                "SafeSpeakFocusVisual");
        XElement focusRectangle = focusStyle
            .Descendants(Presentation + "Rectangle")
            .Single();

        Assert.True(
            double.TryParse(
                focusRectangle.Attribute("StrokeThickness")?.Value,
                out double thickness) && thickness >= 3,
            "SafeSpeakFocusVisual must draw a focus indicator at least 3 DIPs thick.");
        Assert.Equal(
            "{DynamicResource SafeSpeakAccentBrush}",
            focusRectangle.Attribute("Stroke")?.Value);

        string[] missingFocusStyles = TabStops(mainWindow)
            .Where(element => !ResolvesToFocusVisual(appResources, element))
            .Select(Describe)
            .ToArray();

        Assert.True(
            missingFocusStyles.Length == 0,
            "Focusable MainWindow controls without SafeSpeakFocusVisual: " +
            string.Join(", ", missingFocusStyles));
    }

    [Fact]
    public void MainWindowThemeSelectorDisplaysExactlyTheThreeProductThemeNames()
    {
        string viewModel = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "ViewModels",
                "MainViewModel.cs"));
        int start = viewModel.IndexOf(
            "public IReadOnlyList<ThemeChoice> ThemeChoices",
            StringComparison.Ordinal);
        int end = viewModel.IndexOf(
            "public string SpokenGuidanceStatus",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        string themeChoices = viewModel[start..end];
        MatchCollection themeOptions = Regex.Matches(
            themeChoices,
            "new\\(ThemePreference\\.[A-Za-z]+,\\s*\"(?<name>[^\"]+)\",\\s*(?<position>[1-9][0-9]*)\\)");
        string[] displayNames = themeOptions
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        int[] positions = themeOptions
            .Select(match => int.Parse(match.Groups["position"].Value))
            .ToArray();

        Assert.Equal(new[] { "Light", "Dark", "High Contrast" }, displayNames);
        Assert.Equal(new[] { 1, 2, 3 }, positions);
    }

    [Fact]
    public void InstalledNeuralVoicesHideTheInstallActionAndRetainStatus()
    {
        XDocument document = LoadMainWindow();
        XElement installButton = document
            .Descendants(Presentation + "Button")
            .Single(element =>
                element.Attribute("Command")?.Value ==
                "{Binding InstallKokoroCommand}");
        XElement installHost = Assert.IsType<XElement>(installButton.Parent);
        XElement status = document
            .Descendants(Presentation + "TextBlock")
            .Single(element =>
                element.Attribute("Text")?.Value ==
                "{Binding KokoroInstallationStatus}");

        Assert.Equal(
            "{Binding ShowKokoroInstallAction, Converter={StaticResource BoolToVisibilityConverter}}",
            installHost.Attribute("Visibility")?.Value);
        Assert.Null(status.Attribute("Visibility"));

        string viewModel = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "ViewModels",
                "MainViewModel.cs"));
        Assert.Contains(
            "public bool ShowKokoroInstallAction => !IsKokoroInstalled;",
            viewModel);
        Assert.Contains(
            "OnPropertyChanged(nameof(ShowKokoroInstallAction));",
            viewModel);
    }

    [Fact]
    public void MainWindowDisplayedTextDoesNotUseObsoleteProductTerminology()
    {
        XDocument document = LoadMainWindow();
        HashSet<string> displayedAttributeNames =
            new(StringComparer.Ordinal)
            {
                "Content",
                "Header",
                "HelpText",
                "Name",
                "Text",
                "Title",
                "ToolTip"
            };
        string[] obsoleteDisplayText = document.Root!
            .DescendantsAndSelf()
            .Attributes()
            .Where(attribute =>
                displayedAttributeNames.Contains(attribute.Name.LocalName))
            .Where(attribute => Regex.IsMatch(
                attribute.Value,
                @"\b(skip|panic|profile)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Select(attribute =>
                $"{attribute.Name.LocalName}=\"{attribute.Value}\" at " +
                Describe(attribute.Parent!))
            .ToArray();

        Assert.True(
            obsoleteDisplayText.Length == 0,
            "Displayed MainWindow text still contains obsolete terminology: " +
            string.Join("; ", obsoleteDisplayText));
    }

    [Fact]
    public void MainWindow_UsesCompactGrowableSizeAndPrimaryDeckNeverScrolls()
    {
        XDocument document = LoadMainWindow();
        XElement window = document.Root!;

        Assert.Equal("720", window.Attribute("Width")?.Value);
        Assert.Equal("700", window.Attribute("Height")?.Value);
        Assert.Equal("640", window.Attribute("MinWidth")?.Value);
        Assert.Equal("620", window.Attribute("MinHeight")?.Value);
        Assert.Null(window.Attribute("MaxWidth"));
        Assert.Null(window.Attribute("MaxHeight"));

        XElement liveTab = document
            .Descendants(Presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "_Live");
        XElement[] primaryActions = TabStops(document)
            .Where(element => !element.Ancestors(Presentation + "TabItem").Any())
            .Concat(liveTab.Descendants().Where(element =>
                element.Attribute("TabIndex") is not null &&
                int.Parse(element.Attribute("TabIndex")!.Value) <= 8))
            .ToArray();

        Assert.All(
            primaryActions,
            action => Assert.DoesNotContain(
                action.Ancestors(),
                ancestor => ancestor.Name.LocalName == "ScrollViewer"));
    }

    [Fact]
    public void NavigationMakesHearStatusTheFirstPersistentKeyboardStop()
    {
        XDocument document = LoadMainWindow();
        XElement navigation = document
            .Descendants(Presentation + "TabControl")
            .Single(element => element.Attribute("TabIndex")?.Value == "2");
        XElement hearStatus = document
            .Descendants(Presentation + "Button")
            .Single(element =>
                Attribute(element, "AutomationProperties.Name") ==
                "Hear current SafeSpeak status");
        XElement liveTab = document
            .Descendants(Presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "_Live");
        XElement[] liveTabStops = liveTab
            .Descendants()
            .Where(element => element.Attribute("TabIndex") is not null)
            .OrderBy(element => int.Parse(element.Attribute("TabIndex")!.Value))
            .ToArray();
        XElement[] persistentTabStops = TabStops(document)
            .Where(element => !element.Ancestors(Presentation + "TabItem").Any())
            .OrderBy(element => int.Parse(element.Attribute("TabIndex")!.Value))
            .ToArray();

        XElement arm = liveTabStops.Single(element =>
            element.Attribute(Xaml + "Name")?.Value == "ArmToggle");

        Assert.Equal("1", hearStatus.Attribute("TabIndex")?.Value);
        Assert.Equal("2", navigation.Attribute("TabIndex")?.Value);
        Assert.Equal("1", arm.Attribute("TabIndex")?.Value);
        Assert.Equal(hearStatus.Parent, navigation.Parent);
        Assert.True(hearStatus.IsBefore(navigation));
        Assert.Null(hearStatus.Attribute("Visibility"));
        Assert.DoesNotContain(
            hearStatus.Ancestors(),
            ancestor => ancestor.Name.LocalName == "TabItem");
        Assert.DoesNotContain(
            hearStatus.Ancestors(),
            ancestor => ancestor == navigation);

        XElement headerHost = navigation
            .Descendants(Presentation + "UniformGrid")
            .Single(element => element.Attribute("IsItemsHost")?.Value == "True");
        Assert.Equal("1", headerHost.Attribute("Rows")?.Value);
        Assert.Equal("0", hearStatus.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", headerHost.Attribute("Grid.Column")?.Value);
        Assert.Equal("2", navigation.Attribute("Grid.ColumnSpan")?.Value);
        Assert.Equal(hearStatus, persistentTabStops[0]);
        Assert.Equal(navigation, persistentTabStops[1]);
        Assert.Equal(Enumerable.Range(1, 10), liveTabStops
            .Select(element => int.Parse(element.Attribute("TabIndex")!.Value)));

        string[] expectedLiveSequence =
        [
            "ArmToggle",
            "EmergencyStopButton",
            "{Binding PauseButtonAutomationName}",
            "Use automatic playback mode button",
            "Use manual playback mode button",
            "Speak next approved message button",
            "Stop current speech button. Shortcut: Control Alt K.",
            "Clear pending text to speech queue button",
            "Reconnect to live stream source button",
            "Live moderation activity list. Shows incoming messages, safety dispositions, and moderation reasons."
        ];
        Assert.Equal(
            expectedLiveSequence,
            liveTabStops
                .Select(element =>
                    element.Attribute(Xaml + "Name")?.Value ??
                    Attribute(element, "AutomationProperties.Name") ??
                    string.Empty)
                .ToArray());

        Assert.Contains(liveTab.Descendants(), element =>
            element.Attribute("Visibility")?.Value.Contains(
                "ShowAutomaticOrPausedControls",
                StringComparison.Ordinal) == true);
        Assert.Contains(liveTab.Descendants(), element =>
            element.Attribute("Visibility")?.Value.Contains(
                "ShowManualControls",
                StringComparison.Ordinal) == true);
        Assert.Contains(liveTab.Descendants(), element =>
            element.Attribute("Visibility")?.Value.Contains(
                "IsArmed",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ComboBoxesOwnTheirSemanticDarkModeTemplates()
    {
        XDocument appResources = XDocument.Load(
            RepositoryFile("src", "SafeSpeak.App", "App.xaml"),
            LoadOptions.SetLineInfo);
        XElement comboStyle = appResources
            .Descendants(Presentation + "Style")
            .Single(element =>
                element.Attribute("TargetType")?.Value == "ComboBox" &&
                element.Attribute(Xaml + "Key") is null);
        XElement itemStyle = appResources
            .Descendants(Presentation + "Style")
            .Single(element =>
                element.Attribute("TargetType")?.Value == "ComboBoxItem" &&
                element.Attribute(Xaml + "Key") is null);

        Assert.Contains(
            comboStyle.Elements(Presentation + "Setter"),
            setter => setter.Attribute("Property")?.Value == "Template");
        Assert.Contains(
            comboStyle.Descendants().Attributes(),
            attribute => attribute.Value ==
                "{DynamicResource SafeSpeakSurfaceBrush}");
        Assert.Contains(
            comboStyle.Descendants().Attributes(),
            attribute => attribute.Value ==
                "{DynamicResource SafeSpeakTextBrush}");
        Assert.Contains(
            comboStyle.Descendants(Presentation + "Popup"),
            popup => popup.Attribute(Xaml + "Name")?.Value == "PART_Popup");
        Assert.Contains(
            itemStyle.Elements(Presentation + "Setter"),
            setter => setter.Attribute("Property")?.Value == "Template");
    }

    [Fact]
    public void SafetyFilterTestUsesThePipelineWithoutLiveSideEffects()
    {
        string viewModel = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "ViewModels",
                "MainViewModel.cs"));
        int start = viewModel.IndexOf(
            "public async Task TestFilter()",
            StringComparison.Ordinal);
        int end = viewModel.IndexOf(
            "public void OpenVirtualCableGuide()",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        string method = viewModel[start..end];
        Assert.Contains("_moderationTestService.EvaluateAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("LiveFeed", method, StringComparison.Ordinal);
        Assert.DoesNotContain("_ttsQueue", method, StringComparison.Ordinal);
        Assert.DoesNotContain("_auditLogger", method, StringComparison.Ordinal);

        string service = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.Core",
                "Moderation",
                "ModerationTestService.cs"));
        Assert.Contains("_pipeline.ProcessMessageAsync", service, StringComparison.Ordinal);
        Assert.Contains("Author = string.Empty", service, StringComparison.Ordinal);
        Assert.Contains("AuthorTier = AuthorTier.Host", service, StringComparison.Ordinal);
        Assert.DoesNotContain("TtsQueue", service, StringComparison.Ordinal);
        Assert.DoesNotContain("StreamAuditLogger", service, StringComparison.Ordinal);

        XDocument document = LoadMainWindow();
        Assert.Contains(
            document.Descendants().Attributes(),
            attribute => attribute.Value == "{Binding TestFilterCommand}");
        Assert.Contains(
            document.Descendants(Presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value.Contains(
                "not broadcast, queued, added to Live Activity, or logged",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SettingsShowsTheExactAuditPathAndReportsOpenFailure()
    {
        XDocument document = LoadMainWindow();
        Assert.Contains(
            document.Descendants(Presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value ==
                "{Binding AuditLogsDirectoryDisplay, StringFormat=Log folder: {0}}");

        string viewModel = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "ViewModels",
                "MainViewModel.cs"));
        int start = viewModel.IndexOf(
            "public void OpenAuditLogsFolder()",
            StringComparison.Ordinal);
        int end = viewModel.IndexOf(
            "public void RerunAccessibilityWizard()",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        string method = viewModel[start..end];
        Assert.Contains("_auditLogger.LogsDirectory", method, StringComparison.Ordinal);
        Assert.Contains("could not open the logs folder", method, StringComparison.Ordinal);
        Assert.Contains("AnnounceState", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsRequiresExplicitAuditLoggingConsentWithNearbyWarning()
    {
        XDocument document = LoadMainWindow();
        XElement consent = document
            .Descendants(Presentation + "CheckBox")
            .Single(element =>
                Attribute(element, "AutomationProperties.Name") ==
                "Save chat and moderation decisions to a local text log");

        Assert.Equal(
            "{Binding EnableStreamAuditLogging}",
            consent.Attribute("IsChecked")?.Value);
        Assert.DoesNotContain(
            consent.AncestorsAndSelf(),
            element => Attribute(element, "Visibility") is "Collapsed" or "Hidden");
        Assert.Contains(
            document.Descendants(Presentation + "TextBlock"),
            element =>
            {
                string warning = element.Attribute("Text")?.Value ?? string.Empty;
                return warning.Contains("usernames", StringComparison.OrdinalIgnoreCase) &&
                       warning.Contains("raw chat", StringComparison.OrdinalIgnoreCase) &&
                       warning.Contains("blocked text", StringComparison.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public void SettingsExposesSavedSpokenGuidanceAndWindowsSpeechHealth()
    {
        XDocument document = LoadMainWindow();
        Assert.Contains(
            document.Descendants(Presentation + "TextBlock"),
            element =>
                element.Attribute("Text")?.Value == "{Binding SpokenGuidanceStatus}" &&
                Attribute(element, "AutomationProperties.Name") ==
                "{Binding SpokenGuidanceStatus}" &&
                Attribute(element, "AutomationProperties.LiveSetting") == "Polite");

        string viewModel = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "ViewModels",
                "MainViewModel.cs"));
        Assert.Contains("_announcer.IsSpeechAvailable", viewModel, StringComparison.Ordinal);
        Assert.Contains("Windows speech is unavailable", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditLoggingConsentControlsTheConnectedSessionLifecycle()
    {
        string viewModel = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "ViewModels",
                "MainViewModel.cs"));

        int settingStart = viewModel.IndexOf(
            "partial void OnEnableStreamAuditLoggingChanged(bool value)",
            StringComparison.Ordinal);
        int settingEnd = viewModel.IndexOf(
            "partial void OnSpeechRateChanged",
            settingStart,
            StringComparison.Ordinal);
        Assert.True(settingStart >= 0 && settingEnd > settingStart);
        string settingHandler = viewModel[settingStart..settingEnd];
        Assert.Contains("_auditLogger.IsEnabled = value", settingHandler, StringComparison.Ordinal);
        Assert.Contains("value && IsConnected", settingHandler, StringComparison.Ordinal);
        Assert.Contains("_auditLogger.StartSession", settingHandler, StringComparison.Ordinal);

        int connectionStart = viewModel.IndexOf(
            "private void SourceConnector_StateChanged",
            StringComparison.Ordinal);
        int connectionEnd = viewModel.IndexOf(
            "private void SourceConnector_EventReceived",
            connectionStart,
            StringComparison.Ordinal);
        Assert.True(connectionStart >= 0 && connectionEnd > connectionStart);
        string connectionHandler = viewModel[connectionStart..connectionEnd];
        Assert.Contains("_auditLogger.StartSession", connectionHandler, StringComparison.Ordinal);
        Assert.Contains("_auditLogger.EndSession", connectionHandler, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupFocusBeginsOnHearStatusBeforeNavigationAndArm()
    {
        XDocument document = LoadMainWindow();
        XElement hearStatus = document
            .Descendants(Presentation + "Button")
            .Single(element =>
                element.Attribute(Xaml + "Name")?.Value == "HearStatusButton");
        XElement navigation = document
            .Descendants(Presentation + "TabControl")
            .Single(element => element.Attribute("TabIndex")?.Value == "2");
        XElement arm = document
            .Descendants(Presentation + "ToggleButton")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "ArmToggle");

        Assert.Equal("1", hearStatus.Attribute("TabIndex")?.Value);
        Assert.Equal("2", navigation.Attribute("TabIndex")?.Value);
        Assert.Equal("1", arm.Attribute("TabIndex")?.Value);

        string codeBehind = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml.cs"));
        int loadedStart = codeBehind.IndexOf("Loaded +=", StringComparison.Ordinal);
        int emergencyHandlerStart = codeBehind.IndexOf(
            "private void EmergencyStopButton_IsVisibleChanged",
            StringComparison.Ordinal);
        Assert.True(loadedStart >= 0 && emergencyHandlerStart > loadedStart);
        string startupFocus = codeBehind[loadedStart..emergencyHandlerStart];

        Assert.Contains("HearStatusButton.Focus()", startupFocus, StringComparison.Ordinal);
        Assert.Contains(
            "Keyboard.Focus(HearStatusButton)",
            startupFocus,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ArmToggle.Focus()", startupFocus, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Keyboard.Focus(ArmToggle)",
            startupFocus,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationTabHandsFocusToTheSelectedPageAndSupportsShiftTabReturn()
    {
        XDocument document = LoadMainWindow();
        XElement navigation = document
            .Descendants(Presentation + "TabControl")
            .Single(element =>
                element.Attribute(Xaml + "Name")?.Value == "MainNavigation");

        Assert.Equal("2", navigation.Attribute("TabIndex")?.Value);

        string codeBehind = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml.cs"));
        int handlerStart = codeBehind.IndexOf(
            "private void MainWindow_PreviewKeyDown",
            StringComparison.Ordinal);
        int collapseHandlerStart = codeBehind.IndexOf(
            "private void EmergencyStopButton_IsVisibleChanged",
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && collapseHandlerStart > handlerStart);
        string navigationHandlers = codeBehind[handlerStart..collapseHandlerStart];

        Assert.Contains("e.Key != Key.Tab", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.Shift", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("MainNavigation.SelectedItem is TabItem", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("pageEntryControl.IsKeyboardFocusWithin", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("0 => ArmToggle", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("1 => ModerationSlider", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("2 => VoiceCombo", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("3 => ThemeSelector", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("FocusElement(reverse ? HearStatusButton", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", navigationHandlers, StringComparison.Ordinal);
    }

    [Fact]
    public void PageEntrySelectorsExposeSectionCurrentValuePositionAndOperation()
    {
        XDocument document = LoadMainWindow();
        XElement moderation = document
            .Descendants(Presentation + "Slider")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "ModerationSlider");
        XElement voice = document
            .Descendants(Presentation + "ComboBox")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "VoiceCombo");
        XElement theme = document
            .Descendants(Presentation + "ListBox")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "ThemeSelector");

        Assert.Contains(
            "ModerationLevelAccessibleText",
            Attribute(moderation, "AutomationProperties.Name"),
            StringComparison.Ordinal);
        Assert.Contains(
            "Arrow keys",
            Attribute(moderation, "AutomationProperties.HelpText"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "voice",
            Attribute(voice, "AutomationProperties.Name"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Arrow keys",
            Attribute(voice, "AutomationProperties.HelpText"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Theme selector", Attribute(theme, "AutomationProperties.Name"));
        Assert.Equal(
            "{Binding ThemeSelectionAccessibleText}",
            Attribute(theme, "AutomationProperties.ItemStatus"));
        Assert.Contains(
            "Arrow keys",
            Attribute(theme, "AutomationProperties.HelpText"),
            StringComparison.OrdinalIgnoreCase);

        XElement itemStyle = theme
            .Descendants(Presentation + "Style")
            .Single(element => element.Attribute("TargetType")?.Value == "ListBoxItem");
        Assert.Contains(itemStyle.Descendants(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "AutomationProperties.PositionInSet" &&
            setter.Attribute("Value")?.Value == "{Binding Position}");
        Assert.Contains(itemStyle.Descendants(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "AutomationProperties.SizeOfSet" &&
            setter.Attribute("Value")?.Value == "3");

        string narrator = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "Accessibility",
                "IntegratedFocusNarrator.cs"));
        Assert.Contains("DescribeListBoxItem", narrator, StringComparison.Ordinal);
        Assert.Contains("ItemsControl.ItemsControlFromItemContainer", narrator, StringComparison.Ordinal);
        Assert.Contains("option {index + 1} of {owner.Items.Count}", narrator, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.GetHelpText(owner)", narrator, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.GetItemStatus(listBox)", narrator, StringComparison.Ordinal);
        Assert.Contains("\"DisplayName\", \"Name\", \"Title\", \"Id\"", narrator, StringComparison.Ordinal);
    }

    [Fact]
    public void CollapsingArmedControlsReturnsFocusToTheRearmAction()
    {
        XDocument document = LoadMainWindow();
        XElement emergency = document
            .Descendants(Presentation + "Button")
            .Single(element =>
                element.Attribute(Xaml + "Name")?.Value == "EmergencyStopButton");

        Assert.Equal(
            "EmergencyStopButton_IsVisibleChanged",
            emergency.Attribute("IsVisibleChanged")?.Value);

        string codeBehind = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml.cs"));
        Assert.Contains(
            "private void EmergencyStopButton_IsVisibleChanged",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains("ArmToggle.Focus()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Focus(ArmToggle)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void HearStatusAnnouncementCoversCurrentLiveStateAndBroadcastRoute()
    {
        string viewModel = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "ViewModels",
                "MainViewModel.cs"));
        int start = viewModel.IndexOf(
            "public void AnnounceStatusPrivately()",
            StringComparison.Ordinal);
        int end = viewModel.IndexOf(
            "public void AddCustomBlockedTerm()",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        string method = viewModel[start..end];
        Assert.Contains("PlaybackModeStatus", method, StringComparison.Ordinal);
        Assert.Contains("ConnectionStatusText", method, StringComparison.Ordinal);
        Assert.Contains("QueueCount", method, StringComparison.Ordinal);
        Assert.Contains("IsSpeaking", method, StringComparison.Ordinal);
        Assert.Contains("SelectedAudioEndpoint", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedPrivateAudioEndpoint", method, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateMonitor", method, StringComparison.Ordinal);
        Assert.Contains("interrupt: true", method, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TextBlock")]
    [InlineData("Label")]
    [InlineData("CheckBox")]
    [InlineData("ComboBox")]
    [InlineData("ComboBoxItem")]
    [InlineData("ListBox")]
    [InlineData("ListBoxItem")]
    [InlineData("ListView")]
    [InlineData("ListViewItem")]
    public void NativeTextControls_UseSemanticThemeForeground(string targetType)
    {
        XDocument appResources = XDocument.Load(
            RepositoryFile("src", "SafeSpeak.App", "App.xaml"),
            LoadOptions.SetLineInfo);
        XElement style = appResources
            .Descendants(Presentation + "Style")
            .Single(element =>
                element.Attribute("TargetType")?.Value == targetType &&
                element.Attribute(Xaml + "Key") is null);

        Assert.Contains(
            style.Descendants(Presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "Foreground" &&
                setter.Attribute("Value")?.Value ==
                "{DynamicResource SafeSpeakTextBrush}");
    }

    [Fact]
    public void LiveFeedUsesOnlyModeratedSafeDisplayBindings()
    {
        XDocument document = LoadMainWindow();
        XElement feed = document
            .Descendants(Presentation + "ListView")
            .Single(element =>
                element.Attribute("ItemsSource")?.Value == "{Binding LiveFeed}");
        string[] boundProperties = feed
            .DescendantsAndSelf()
            .Attributes()
            .Select(attribute => attribute.Value)
            .SelectMany(BindingProperties)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("SafeAuthorDisplayName", boundProperties);
        Assert.Contains("SafeDisplayText", boundProperties);
        Assert.Contains("SafeReasonDescription", boundProperties);
        Assert.DoesNotContain("AuthorDisplayName", boundProperties);
        Assert.DoesNotContain("DisplayText", boundProperties);
        Assert.DoesNotContain("RawText", boundProperties);

        XElement feedItemStyle = document
            .Descendants(Presentation + "Style")
            .Single(element => element.Attribute(Xaml + "Key")?.Value == "FeedItem");
        Assert.Contains(
            feedItemStyle.Descendants(Presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "AutomationProperties.Name" &&
                setter.Attribute("Value")?.Value == "{Binding AccessibleSummary}");
    }

    private static XDocument LoadMainWindow() =>
        XDocument.Load(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml"),
            LoadOptions.SetLineInfo);

    private static XElement[] TabStops(XDocument document) =>
        document
            .Descendants()
            .Where(element => element.Attribute("TabIndex") is not null)
            .ToArray();

    private static XElement NamedElement(
        XDocument document,
        string elementName,
        string xName) =>
        document
            .Descendants(Presentation + elementName)
            .Single(element => element.Attribute(Xaml + "Name")?.Value == xName);

    private static bool HasAccessibleName(
        XDocument document,
        XElement element)
    {
        if (!string.IsNullOrWhiteSpace(
                Attribute(element, "AutomationProperties.Name")) ||
            !string.IsNullOrWhiteSpace(element.Attribute("Content")?.Value))
        {
            return true;
        }

        string? xName = element.Attribute(Xaml + "Name")?.Value;
        return xName is not null && document
            .Descendants(Presentation + "Label")
            .Any(label =>
                label.Attribute("Target")?.Value.Contains(
                    $"ElementName={xName}",
                    StringComparison.Ordinal) == true);
    }

    private static bool ResolvesToFocusVisual(
        XDocument appResources,
        XElement control)
    {
        string typeName = control.Name.LocalName;
        string? styleReference = control.Attribute("Style")?.Value;

        if (styleReference is not null)
        {
            Match resource = Regex.Match(
                styleReference,
                @"\{StaticResource\s+(?<key>[^}]+)\}");
            if (resource.Success)
            {
                XElement? keyedStyle = appResources
                    .Descendants(Presentation + "Style")
                    .SingleOrDefault(element =>
                        element.Attribute(Xaml + "Key")?.Value ==
                        resource.Groups["key"].Value);
                if (keyedStyle is not null && HasFocusVisualSetter(keyedStyle))
                {
                    return true;
                }
            }
        }

        return appResources
            .Descendants(Presentation + "Style")
            .Where(element => element.Attribute(Xaml + "Key") is null)
            .Where(element => element.Attribute("TargetType")?.Value == typeName)
            .Any(HasFocusVisualSetter);
    }

    private static bool HasFocusVisualSetter(XElement style) =>
        style
            .Descendants(Presentation + "Setter")
            .Any(setter =>
                setter.Attribute("Property")?.Value == "FocusVisualStyle" &&
                setter.Attribute("Value")?.Value ==
                "{StaticResource SafeSpeakFocusVisual}");

    private static IEnumerable<string> BindingProperties(string value)
    {
        foreach (Match match in Regex.Matches(
                     value,
                     @"\{Binding\s+(?<property>[A-Za-z][A-Za-z0-9]*)"))
        {
            yield return match.Groups["property"].Value;
        }
    }

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == localName)
            ?.Value;

    private static string Describe(XElement element)
    {
        IXmlLineInfo lineInfo = element;
        string name =
            element.Attribute(Xaml + "Name")?.Value ??
            element.Name.LocalName;
        string? tabIndex = element.Attribute("TabIndex")?.Value;
        string suffix = tabIndex is null ? string.Empty : $" TabIndex {tabIndex}";
        return $"{name}{suffix} (MainWindow.xaml line {lineInfo.LineNumber})";
    }

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
