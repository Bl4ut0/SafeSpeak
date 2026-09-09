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
            "Label",
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
            .Single(element => element.Attribute("Header")?.Value == "Live");
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
    public void ComboBoxesRequireExplicitOpeningBeforeArrowSelection()
    {
        XDocument document = LoadMainWindow();
        XElement[] comboBoxes = document.Descendants(Presentation + "ComboBox").ToArray();

        Assert.NotEmpty(comboBoxes);
        Assert.All(comboBoxes, combo =>
        {
            Assert.Equal(
                "SelectionComboBox_PreviewKeyDown",
                combo.Attribute("PreviewKeyDown")?.Value);
            Assert.Contains(
                "open",
                Attribute(combo, "AutomationProperties.HelpText"),
                StringComparison.OrdinalIgnoreCase);
        });

        string codeBehind = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml.cs"));
        Assert.Contains(
            "private void SelectionComboBox_PreviewKeyDown",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains("comboBox.IsDropDownOpen = true;", codeBehind);
        Assert.Contains("Key.Left or Key.Right or Key.Up or Key.Down", codeBehind);
    }

    [Fact]
    public void MouseWheelAlwaysScrollsTheActivePageInsteadOfNestedControls()
    {
        XDocument document = LoadMainWindow();
        Assert.NotNull(NamedElement(document, "ScrollViewer", "SafetyPageScrollViewer"));
        Assert.NotNull(NamedElement(document, "ScrollViewer", "VoicePageScrollViewer"));
        Assert.NotNull(NamedElement(document, "ScrollViewer", "SettingsPageScrollViewer"));

        string codeBehind = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml.cs"));
        Assert.Contains("PreviewMouseWheel += MainWindow_PreviewMouseWheel", codeBehind);
        Assert.Contains("1 => SafetyPageScrollViewer", codeBehind);
        Assert.Contains("2 => VoicePageScrollViewer", codeBehind);
        Assert.Contains("3 => SettingsPageScrollViewer", codeBehind);
        Assert.Contains("pageScroller.ScrollToVerticalOffset", codeBehind);
        Assert.Contains("e.Handled = true", codeBehind);
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
    public void MainWindow_UsesTwoColumnLiveWorkspace()
    {
        XDocument document = LoadMainWindow();
        XElement window = document.Root!;

        Assert.Equal("1080", window.Attribute("Width")?.Value);
        Assert.Equal("700", window.Attribute("Height")?.Value);
        Assert.Equal("760", window.Attribute("MinWidth")?.Value);
        Assert.Equal("620", window.Attribute("MinHeight")?.Value);
        Assert.Null(window.Attribute("MaxWidth"));
        Assert.Null(window.Attribute("MaxHeight"));

        XElement liveTab = document
            .Descendants(Presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "Live");
        XElement liveGrid = liveTab.Elements(Presentation + "Grid").Single();
        XElement[] columns = liveGrid
            .Element(Presentation + "Grid.ColumnDefinitions")!
            .Elements(Presentation + "ColumnDefinition")
            .ToArray();
        Assert.Equal(new[] { "2*", "12", "3*" },
            columns.Select(column => column.Attribute("Width")?.Value));

        XElement feed = NamedElement(document, "ListView", "LiveFeedListView");
        Assert.Equal("2", feed.Parent?.Attribute("Grid.Column")?.Value);
        XElement arm = NamedElement(document, "ToggleButton", "ArmToggle");
        Assert.Empty(liveTab.Descendants(Presentation + "ScrollViewer"));
        Assert.Contains(arm.Ancestors(), ancestor =>
            ancestor.Name.LocalName == "StackPanel" &&
            ancestor.Attribute("Grid.Column")?.Value == "0");

        XElement sourceStatus = liveTab.Descendants(Presentation + "TextBlock")
            .Single(element =>
                Attribute(element, "AutomationProperties.Name") ==
                "{Binding ConnectionStatusText}");
        Assert.Equal("{Binding ConnectionSummaryText}",
            sourceStatus.Attribute("Text")?.Value);
        Assert.Equal("CharacterEllipsis", sourceStatus.Attribute("TextTrimming")?.Value);
        XElement compactValueStyle = document.Descendants(Presentation + "Style")
            .Single(element => element.Attribute(Xaml + "Key")?.Value == "LiveStatusValue");
        Assert.Contains(compactValueStyle.Descendants(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "TextWrapping" &&
            setter.Attribute("Value")?.Value == "NoWrap");

        XElement connectors = liveTab.Descendants(Presentation + "ItemsControl")
            .Single(element => element.Attribute("ItemsSource")?.Value == "{Binding LiveConnectors}");
        XElement connectorToggle = connectors.Descendants(Presentation + "CheckBox").Single();
        Assert.Equal("{Binding IsEnabled, Mode=OneWay}",
            connectorToggle.Attribute("IsChecked")?.Value);
        Assert.Equal(
            "{Binding DataContext.ToggleLiveConnectorCommand, RelativeSource={RelativeSource AncestorType=Window}}",
            connectorToggle.Attribute("Command")?.Value);
        Assert.Equal("10", connectorToggle.Attribute("TabIndex")?.Value);
        Assert.Equal("Local", Attribute(connectors, "KeyboardNavigation.TabNavigation"));
        Assert.Contains(connectors.Descendants(Presentation + "DataTrigger"), trigger =>
            trigger.Attribute("Binding")?.Value == "{Binding State}" &&
            trigger.Attribute("Value")?.Value == "Connected");
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
            .Single(element => element.Attribute("Header")?.Value == "Live");
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
        Assert.Equal(Enumerable.Range(1, 13), liveTabStops
            .Select(element => int.Parse(element.Attribute("TabIndex")!.Value)));

        string[] expectedLiveSequence =
        [
            "ArmToggle",
            "EmergencyStopButton",
            "PauseOrResumeButton",
            "UseAutomaticPlaybackButton",
            "UseManualPlaybackButton",
            "SpeakNextApprovedMessageButton",
            "{Binding StopCurrentSpeechAutomationName}",
            "Clear pending text to speech queue button",
            "Reconnect all enabled live connectors button",
            "{Binding ToggleName}",
            "Hear live activity review status button",
            "{Binding FilteredContentToggleAutomationName}",
            "LiveFeedListView"
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
    public void SpokenAttributionIncludesPlatformOnlyWhenMultipleConnectorsAreEnabled()
    {
        string main = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "ViewModels", "MainViewModel.cs"));

        Assert.Contains(
            "_connectorSessions.Count(connector =>",
            main,
            StringComparison.Ordinal);
        Assert.Contains(
            "connector.IsConfigured && connector.IsEnabled) > 1",
            main,
            StringComparison.Ordinal);
        Assert.Contains(
            "IncludePlatformInSpeech\n                        ? SpokenAttributionStyle.SaysOnPlatform\n                        : SpokenAttributionStyle.Says",
            main.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains(
            "IncludePlatformInSpeech\n                    ? SpokenAttributionStyle.LeadingNameOnPlatform\n                    : SpokenAttributionStyle.LeadingName",
            main.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
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
    public void SafetyPage_CanMoveNarratedGuideControlsWithoutHidingVisibleText()
    {
        XDocument document = LoadMainWindow();
        XElement readGuide = NamedElement(
            document,
            "Button",
            "ReadModerationGuideButton");
        XElement guidePanel = NamedElement(
            document,
            "Border",
            "ModerationGuidePanel");
        XElement filterTest = NamedElement(
            document,
            "TextBox",
            "FilterTestInput");
        XElement safetyTab = document.Descendants(Presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "Safety");
        XElement controlsTop = NamedElement(document, "Border", "SafetyGuideControlsTop");
        XElement controlsEnd = NamedElement(document, "Border", "SafetyGuideControlsEnd");
        XElement readGuideAtEnd = NamedElement(
            document,
            "Button",
            "ReadModerationGuideButtonAtEnd");

        Assert.Equal("1", readGuide.Attribute("TabIndex")?.Value);
        Assert.Equal(
            "{Binding ReadModerationGuideCommand}",
            readGuide.Attribute("Command")?.Value);
        Assert.Contains(
            "Play the whole Safety guide",
            Attribute(readGuide, "AutomationProperties.Name"),
            StringComparison.Ordinal);
        Assert.Contains(
            "Shut up built-in guidance",
            Attribute(readGuide, "AutomationProperties.HelpText"),
            StringComparison.Ordinal);
        Assert.True(readGuide.IsBefore(filterTest));
        Assert.True(filterTest.IsBefore(guidePanel));
        Assert.True(guidePanel.IsBefore(readGuideAtEnd));
        Assert.Null(guidePanel.Attribute("Visibility"));
        Assert.Equal(
            "{Binding AreSafetyGuideControlsAtTop, Converter={StaticResource BoolToVisibilityConverter}}",
            controlsTop.Attribute("Visibility")?.Value);
        Assert.Equal(
            "{Binding AreSafetyGuideControlsAtEnd, Converter={StaticResource BoolToVisibilityConverter}}",
            controlsEnd.Attribute("Visibility")?.Value);
        Assert.Equal(Enumerable.Range(1, 27), safetyTab.Descendants()
            .Where(element => element.Attribute("TabIndex") is not null)
            .Select(element => int.Parse(element.Attribute("TabIndex")!.Value))
            .OrderBy(value => value));
        Assert.Equal("2", NamedElement(document, "Button", "ReadModerationGuidePageButton")
            .Attribute("TabIndex")?.Value);
        Assert.Equal("3", NamedElement(document, "Button", "ResetModerationGuideButton")
            .Attribute("TabIndex")?.Value);
        Assert.Equal("4", NamedElement(document, "Button", "ToggleSafetyGuideButton")
            .Attribute("TabIndex")?.Value);
        Assert.Equal("24", readGuideAtEnd.Attribute("TabIndex")?.Value);
        Assert.Equal("27", NamedElement(document, "Button", "ToggleSafetyGuideButtonAtEnd")
            .Attribute("TabIndex")?.Value);
        Assert.Equal("GuidePlacementButton_Click",
            NamedElement(document, "Button", "ToggleSafetyGuideButton")
                .Attribute("Click")?.Value);
        Assert.Single(document.Descendants(Presentation + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{Binding ModerationLevelDescription}" &&
            element.Ancestors().Contains(guidePanel));

        string window = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml"));
        string viewModel = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "ViewModels", "MainViewModel.cs"));
        string service = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.Core", "Moderation", "ModerationTestService.cs"));

        Assert.Contains("changes only the minimum contextual-hostility score", window);
        Assert.Contains("does not see earlier chat messages", window);
        Assert.Contains("not a judgment of the sender's motive", window);
        Assert.Contains("{Binding ModerationLevelGuide}", window);
        Assert.Contains("{Binding ContextualIntentSignals}", window);
        Assert.Contains("{Binding AcceptedHostilityExamples}", window);
        Assert.Contains("{Binding RejectedHostilityExamples}", window);
        Assert.Contains("{Binding SliderDependentHostilityExamples}", window);
        Assert.Contains("{Binding SensitiveContextExamples}", window);
        Assert.Contains("{Binding AlwaysOnModerationLayers}", window);
        Assert.DoesNotContain("Intent situations", window, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ModerationScenario", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("ModerationTestScenario", service, StringComparison.Ordinal);
        Assert.Contains("Level 1, Relaxed — 90% cutoff", viewModel);
        Assert.Contains("Level 4, Maximum — 45% cutoff", viewModel);
        Assert.Contains("public string ModerationGuideNarration", viewModel);
        Assert.Contains("public void ReadModerationGuide()", viewModel);
        Assert.Contains("AnnounceNarration(", viewModel);
        Assert.Contains("ModerationGuideNarration,", viewModel);
        Assert.Contains("ReadNextModerationGuideSection", viewModel);
        Assert.Contains("Safety guide controls moved to the bottom of the page", viewModel);
        Assert.Contains("The button you pressed moved with them", viewModel);
        Assert.Contains("Navigate to the bottom of the Safety page to find it again", viewModel);
        Assert.DoesNotContain("Hide guide", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Show guide", viewModel, StringComparison.Ordinal);
        Assert.Contains(".Concat(SensitiveContextExamples)", viewModel);
        Assert.Contains(".Concat(AlwaysOnModerationLayers)", viewModel);
        Assert.Contains("I hate this game.", viewModel);
        Assert.Contains("I hate this stream.", viewModel);
        Assert.Contains("I hate you.", viewModel);
        Assert.Contains("I'm excited to do things with my niece.", viewModel);
        Assert.Contains("Sexual abuse of children is wrong.", viewModel);
    }

    [Fact]
    public void SafetyPage_ManagesOptionalQwenModelWithoutAnExternalInstall()
    {
        XDocument document = LoadMainWindow();
        XElement selector = NamedElement(document, "ComboBox", "ModerationModelSelector");
        XElement install = NamedElement(document, "Button", "InstallQwenModelButton");
        XElement cancel = NamedElement(document, "Button", "CancelQwenModelInstallButton");
        XElement remove = NamedElement(document, "Button", "RemoveQwenModelButton");
        string window = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml"));
        string viewModel = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "ViewModels",
                "MainViewModel.ModerationModel.cs"));
        string runtime = File.ReadAllText(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "Services",
                "Qwen3GuardRuntimeManager.cs"));

        Assert.Contains("no other application or terminal command is required",
            Attribute(selector, "AutomationProperties.HelpText"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{Binding InstallQwenModelCommand}", install.Attribute("Command")?.Value);
        Assert.Equal("{Binding CancelQwenModelInstallCommand}", cancel.Attribute("Command")?.Value);
        Assert.Equal("{Binding RemoveQwenModelCommand}", remove.Attribute("Command")?.Value);
        Assert.Contains("ModerationModelDownloadProgress", window);
        Assert.Contains("ModerationModelDownloadStatus", window);
        Assert.Contains("InstallModelAsync", viewModel);
        Assert.Contains("CancelQwenModelInstall", viewModel);
        Assert.Contains("RemoveModelAsync", viewModel);
        Assert.Contains("OLLAMA_NO_CLOUD", runtime);
        Assert.Contains("127.0.0.1", runtime);
        Assert.Contains("ModelBlobSha256", runtime);
        Assert.DoesNotContain("Install Ollama and run ollama pull", window);
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
        Assert.Contains("1 => SafetyModerationChapterHeading", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("2 => VoiceSelectionChapterHeading", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("3 => SettingsSourceChapterHeading", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("FocusElement(reverse ? HearStatusButton", navigationHandlers, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", navigationHandlers, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_AccessKeysAreUniqueAndOnlyFixedShortcutsOpenPages()
    {
        XDocument document = LoadMainWindow();
        string[] accessKeys = document
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute =>
                attribute.Name.LocalName is "Content" or "Header")
            .SelectMany(attribute => Regex.Matches(
                attribute.Value,
                "_(?<key>[A-Za-z0-9])").Select(match =>
                    match.Groups["key"].Value.ToUpperInvariant()))
            .ToArray();

        Assert.Empty(accessKeys
            .GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1));

        XElement navigation = NamedElement(document, "TabControl", "MainNavigation");
        Assert.Contains(
            "Control plus 1 through 4",
            Attribute(navigation, "AutomationProperties.Name"),
            StringComparison.Ordinal);
        Assert.Contains(
            "Alt plus a number jumps directly to that chapter",
            Attribute(navigation, "AutomationProperties.HelpText"),
            StringComparison.Ordinal);

        string codeBehind = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml.cs"));
        Assert.Contains("Key.D1 or Key.NumPad1", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Key.D4 or Key.NumPad4", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("key == Key.H", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("key == Key.Space", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SelectNavigationTab", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackModeSwitchAndLiveFeedReviewPreserveKeyboardContext()
    {
        XDocument document = LoadMainWindow();
        XElement automatic = NamedElement(
            document,
            "Button",
            "UseAutomaticPlaybackButton");
        XElement manual = NamedElement(
            document,
            "Button",
            "UseManualPlaybackButton");
        XElement feed = NamedElement(document, "ListView", "LiveFeedListView");

        Assert.Equal("PlaybackModeButton_Click", automatic.Attribute("Click")?.Value);
        Assert.Equal("PlaybackModeButton_Click", manual.Attribute("Click")?.Value);
        Assert.Equal(
            "LiveFeedListView_GotKeyboardFocus",
            feed.Attribute("GotKeyboardFocus")?.Value);
        Assert.Equal(
            "LiveFeedListView_LostKeyboardFocus",
            feed.Attribute("LostKeyboardFocus")?.Value);
        Assert.Equal(
            "{Binding LiveFeedReviewStatus}",
            Attribute(feed, "AutomationProperties.ItemStatus"));
        Assert.Contains(
            "moderation and text to speech continue",
            Attribute(feed, "AutomationProperties.HelpText"),
            StringComparison.Ordinal);

        string codeBehind = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml.cs"));
        Assert.Contains("SpeakNextApprovedMessageButton", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PauseOrResumeButton", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LiveFeedListView.IsKeyboardFocusWithin", codeBehind, StringComparison.Ordinal);
        Assert.Contains("viewModel.PauseLiveFeedReview()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("viewModel.ResumeLiveFeedReview()", codeBehind, StringComparison.Ordinal);

        string viewModel = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("public void PauseLiveFeedReview()", viewModel, StringComparison.Ordinal);
        Assert.Contains("public void ResumeLiveFeedReview()", viewModel, StringComparison.Ordinal);
        Assert.Contains("_heldLiveFeedDecisions.Add(decision)", viewModel, StringComparison.Ordinal);
        Assert.Contains("AddDecisionToLiveFeed(decision)", viewModel, StringComparison.Ordinal);
        Assert.Contains("moderation and speech continue", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomTermListsGrowForLargeTextInsteadOfUsingRestrictiveMaximumHeights()
    {
        XDocument document = LoadMainWindow();
        foreach (string name in new[] { "CustomBlockedTermsList", "CustomAllowedTermsList" })
        {
            XElement list = NamedElement(document, "ListBox", name);
            Assert.Equal("80", list.Attribute("MinHeight")?.Value);
            Assert.Null(list.Attribute("MaxHeight"));
        }
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
    public void VolumeSlidersSupportKeyboardBoostAndExposeCurrentValues()
    {
        XDocument document = LoadMainWindow();
        foreach (string name in new[] { "VolumeSlider", "ReaderVolumeSlider" })
        {
            XElement slider = NamedElement(document, "Slider", name);
            Assert.Equal("0", slider.Attribute("Minimum")?.Value);
            Assert.Equal("150", slider.Attribute("Maximum")?.Value);
            Assert.Equal("5", slider.Attribute("SmallChange")?.Value);
            Assert.Equal("10", slider.Attribute("LargeChange")?.Value);
            Assert.Equal("True", slider.Attribute("IsSnapToTickEnabled")?.Value);
            Assert.Contains("Left and Right Arrow keys",
                Attribute(slider, "AutomationProperties.HelpText"),
                StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(document.Descendants(Presentation + "TextBlock"), element =>
            element.Attribute("Text")?.Value ==
            "{Binding ReaderSpeechVolume, StringFormat={}{0}%}");
    }

    [Fact]
    public void ThemeSelectorIsOneTabStopAndDetailedNarratorChoiceIsExplicit()
    {
        XDocument document = LoadMainWindow();
        XElement theme = NamedElement(document, "ListBox", "ThemeSelector");
        XElement itemStyle = theme.Descendants(Presentation + "Style").Single();

        Assert.Equal("ThemeSelector_PreviewKeyDown",
            theme.Attribute("PreviewKeyDown")?.Value);
        Assert.Equal("ThemeSelector_PreviewMouseLeftButtonDown",
            theme.Attribute("PreviewMouseLeftButtonDown")?.Value);
        Assert.Contains(itemStyle.Descendants(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "Focusable" &&
            setter.Attribute("Value")?.Value == "False");
        Assert.Equal("True", theme.Attribute("Focusable")?.Value);
        Assert.Equal("True", theme.Attribute("IsTabStop")?.Value);
        Assert.Contains(itemStyle.Descendants(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "IsTabStop" &&
            setter.Attribute("Value")?.Value == "False");

        XElement detailed = document.Descendants(Presentation + "CheckBox").Single(element =>
            element.Attribute("IsChecked")?.Value ==
            "{Binding NarrateDetailedHelp, Mode=TwoWay}");
        Assert.Equal("Detailed narrator descriptions", detailed.Attribute("Content")?.Value);
        Assert.Contains("only the control name",
            Attribute(detailed, "AutomationProperties.HelpText"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsKeyboardFlowTraversesEveryVisibleEnabledTabStopInOrder()
    {
        XDocument document = LoadMainWindow();
        XElement settingsTab = document.Descendants(Presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "Settings");
        XElement settingsPanel = settingsTab.Descendants(Presentation + "StackPanel")
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "SettingsPanel");
        XElement visibleGuide = NamedElement(document, "Border", "SettingsGuidePanel");
        XElement runSetup = settingsTab.Descendants(Presentation + "Button")
            .Single(element => element.Attribute("Content")?.Value == "Run Setup Again");
        XElement guideButtonsAtEnd = NamedElement(
            document,
            "Border",
            "SettingsGuideControlsEnd");
        XElement[] stops = settingsTab.Descendants()
            .Where(element => element.Attribute("TabIndex") is not null)
            .OrderBy(element => int.Parse(element.Attribute("TabIndex")!.Value))
            .ToArray();

        Assert.Equal("SettingsPanel_PreviewKeyDown",
            settingsPanel.Attribute("PreviewKeyDown")?.Value);
        Assert.Equal(Enumerable.Range(1, 54), stops.Select(element =>
            int.Parse(element.Attribute("TabIndex")!.Value)));
        Assert.Equal("SettingsGuideButton", stops[0].Attribute(Xaml + "Name")?.Value);
        Assert.Equal("ReadSettingsGuidePageButton", stops[1].Attribute(Xaml + "Name")?.Value);
        Assert.Equal("ResetSettingsGuideButton", stops[2].Attribute(Xaml + "Name")?.Value);
        Assert.Equal("ToggleSettingsGuideButton", stops[3].Attribute(Xaml + "Name")?.Value);
        Assert.Equal("SettingsSourceChapterHeading", stops[4].Attribute(Xaml + "Name")?.Value);
        Assert.Equal("ThemeSelector", stops[6].Attribute(Xaml + "Name")?.Value);
        Assert.Equal("SpokenGuidanceToggle", stops[7].Attribute(Xaml + "Name")?.Value);
        Assert.Equal("Run Setup Again", stops[48].Attribute("Content")?.Value);
        Assert.Equal("SettingsGuideButtonAtEnd", stops[50].Attribute(Xaml + "Name")?.Value);
        Assert.Equal("ToggleSettingsGuideButtonAtEnd", stops[^1].Attribute(Xaml + "Name")?.Value);
        Assert.Equal("{Binding ConfiguredConnectors}",
            NamedElement(document, "ItemsControl", "ConfiguredConnectorCards")
                .Attribute("ItemsSource")?.Value);
        Assert.Equal("{Binding AvailableConnectors}",
            NamedElement(document, "ItemsControl", "AvailableConnectorCards")
                .Attribute("ItemsSource")?.Value);
        Assert.True(
            NamedElement(document, "ItemsControl", "ConfiguredConnectorCards")
                .IsBefore(NamedElement(document, "ItemsControl", "AvailableConnectorCards")));
        Assert.Equal("Collapsed",
            NamedElement(document, "Border", "InlineConnectorCapturePanel")
                .Attribute("Visibility")?.Value);
        Assert.Equal("Collapsed",
            NamedElement(document, "Border", "InlineConnectorManagementPanel")
                .Attribute("Visibility")?.Value);
        Assert.Equal("2",
            NamedElement(document, "ItemsControl", "ConfiguredConnectorCards")
                .Descendants(Presentation + "UniformGrid")
                .Single()
                .Attribute("Columns")?.Value);
        Assert.Equal("2",
            NamedElement(document, "ItemsControl", "AvailableConnectorCards")
                .Descendants(Presentation + "UniformGrid")
                .Single()
                .Attribute("Columns")?.Value);
        Assert.True(runSetup.IsBefore(visibleGuide));
        Assert.True(visibleGuide.IsBefore(guideButtonsAtEnd));

        string codeBehind = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml.cs"));
        string shortcutsViewModel = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "ViewModels", "MainViewModel.Shortcuts.cs"));
        Assert.Contains("EnumerateVisualDescendants(SettingsPanel)", codeBehind);
        Assert.Contains(".OrderBy(GetSettingsNavigationOrder)", codeBehind);
        Assert.Contains("SettingsConnectorCard", codeBehind);
        Assert.Contains("SettingsConnectorInlineControl", codeBehind);
        Assert.Contains("ConfigureTikTokDirectAsync(username)", codeBehind);
        Assert.Contains("_firstConnectorUsername", codeBehind);
        Assert.Contains("BeginInlineConnectorManagement(connector)", codeBehind);
        Assert.Contains("InlineConnectorEditButton_Click", codeBehind);
        Assert.Contains("InlineConnectorDeleteButton_Click", codeBehind);
        string mainViewModel = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("OrderBy(item => item.DisplayName", mainViewModel);
        Assert.Contains("bool isEnabled = isConfigured", mainViewModel);
        Assert.Contains("connect automatically the next time SafeSpeak starts", mainViewModel);
        Assert.Contains("control.IsVisible", codeBehind);
        Assert.Contains("control.IsEnabled", codeBehind);
        Assert.Contains("FocusElement(HearStatusButton)", codeBehind);
        Assert.Contains("3 => SettingsSourceChapterHeading", codeBehind);
        Assert.Contains("GuidePlacementButton_Click", codeBehind);
        Assert.Contains("Focus is now on {focusName}", codeBehind);
        Assert.Contains("Move controls to bottom", shortcutsViewModel);
        Assert.Contains("Move controls to top", shortcutsViewModel);
        Assert.Contains("Settings guide controls moved to the bottom of the page", shortcutsViewModel);
        Assert.Contains("The button you pressed moved with them", shortcutsViewModel);
        Assert.Contains("Navigate to the bottom of the Settings page to find it again", shortcutsViewModel);
        Assert.DoesNotContain("Hide guide", shortcutsViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Show guide", shortcutsViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySettingsTabStopProvidesNarratorHelpOrAFullReadAction()
    {
        XDocument document = LoadMainWindow();
        XElement settingsTab = document.Descendants(Presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "Settings");
        string[] missingHelp = settingsTab.Descendants()
            .Where(element => element.Attribute("TabIndex") is not null)
            .Where(element => string.IsNullOrWhiteSpace(
                Attribute(element, "AutomationProperties.HelpText")))
            .Select(Describe)
            .ToArray();

        Assert.True(
            missingHelp.Length == 0,
            "Every Settings tab stop must explain itself to the detailed built-in narrator. Missing: " +
            string.Join(", ", missingHelp));
    }

    [Fact]
    public void SecondaryPagesExposeFocusableNumberedChapterHeadings()
    {
        XDocument document = LoadMainWindow();
        XDocument appResources = XDocument.Load(
            RepositoryFile("src", "SafeSpeak.App", "App.xaml"));
        XElement liveTab = document.Descendants(Presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "Live");
        XElement safetyTab = document.Descendants(Presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "Safety");
        XElement voiceTab = document.Descendants(Presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "Voice");
        XElement settingsTab = document.Descendants(Presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "Settings");

        XElement[] liveChapters = ChapterHeadings(liveTab);
        XElement[] safetyChapters = ChapterHeadings(safetyTab);
        XElement[] voiceChapters = ChapterHeadings(voiceTab);
        XElement[] settingsChapters = ChapterHeadings(settingsTab);

        Assert.Empty(liveChapters);
        Assert.Equal(4, safetyChapters.Length);
        Assert.Equal(4, voiceChapters.Length);
        Assert.Equal(9, settingsChapters.Length);
        XElement chapterStyle = appResources.Descendants(Presentation + "Style")
            .Single(element => element.Attribute(Xaml + "Key")?.Value == "ChapterHeading");
        Assert.Contains(chapterStyle.Descendants(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "AutomationProperties.HeadingLevel" &&
            setter.Attribute("Value")?.Value == "Level2");
        Assert.Contains(chapterStyle.Descendants(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "Focusable" &&
            setter.Attribute("Value")?.Value == "True");
        Assert.True(HasFocusVisualSetter(chapterStyle));
        Assert.All(safetyChapters.Concat(voiceChapters).Concat(settingsChapters), heading =>
        {
            Assert.Equal("{StaticResource ChapterHeading}", heading.Attribute("Style")?.Value);
            Assert.StartsWith("Chapter ", heading.Attribute("Content")?.Value, StringComparison.Ordinal);
            Assert.Contains("chapter", Attribute(heading, "AutomationProperties.Name"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Press Tab to enter this chapter",
                Attribute(heading, "AutomationProperties.HelpText"),
                StringComparison.Ordinal);
            Assert.Contains("Alt plus",
                Attribute(heading, "AutomationProperties.HelpText"),
                StringComparison.Ordinal);
        });

        string narrator = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "Accessibility", "IntegratedFocusNarrator.cs"));
        Assert.Contains("AutomationProperties.GetHeadingLevel", narrator, StringComparison.Ordinal);
        Assert.Contains("chapter heading", narrator, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingWindowNeverWaitsForBackendCleanup()
    {
        string codeBehind = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml.cs"));
        Assert.DoesNotContain("e.Cancel = true", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.WhenAny", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ShutdownTimeout", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Task.Run(async () => await vm.DisposeAsync()", codeBehind,
            StringComparison.Ordinal);
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
        Assert.Contains("_announcer.AnnounceOnDemand", method, StringComparison.Ordinal);
        Assert.Contains("interrupt: true", method, StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltInGuidanceClearlyDisclosesSeparateAudioRoutingAndDoubleSpeechRisk()
    {
        XDocument document = LoadMainWindow();
        XElement guidanceToggle = document
            .Descendants(Presentation + "CheckBox")
            .Single(element =>
                element.Attribute("IsChecked")?.Value ==
                "{Binding SpokenGuidanceEnabled, Mode=TwoWay}");
        string help = Attribute(guidanceToggle, "AutomationProperties.HelpText") ?? string.Empty;

        Assert.Contains("two voices", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("selected guidance audio device", help, StringComparison.Ordinal);

        XElement output = NamedElement(document, "ComboBox", "GuidanceOutputCombo");
        Assert.Equal("{Binding SelectedGuidanceAudioEndpoint, Mode=TwoWay}",
            output.Attribute("SelectedValue")?.Value);
        Assert.Contains("does not change livestream text to speech",
            Attribute(output, "AutomationProperties.HelpText"),
            StringComparison.OrdinalIgnoreCase);
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
    public void LiveFeedRequiresDeliberateActivationBeforeShowingFilteredText()
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

        Assert.Contains("ReviewAuthorDisplayName", boundProperties);
        Assert.Contains("PlatformName", boundProperties);
        Assert.Contains("ReviewDisplayText", boundProperties);
        Assert.Contains("SafeReasonDescription", boundProperties);
        Assert.Contains("RevealInstruction", boundProperties);
        Assert.DoesNotContain("RawAuthorDisplayName", boundProperties);
        Assert.DoesNotContain("DisplayText", boundProperties);
        Assert.DoesNotContain("RawText", boundProperties);

        Assert.Equal("LiveFeedListView_MouseDoubleClick",
            feed.Attribute("MouseDoubleClick")?.Value);
        Assert.Equal("LiveFeedListView_PreviewKeyDown",
            feed.Attribute("PreviewKeyDown")?.Value);
        Assert.Contains("double-click or press Enter",
            Attribute(feed, "AutomationProperties.HelpText"),
            StringComparison.Ordinal);
        XElement revealAll = document.Descendants(Presentation + "Button")
            .Single(element => element.Attribute("Command")?.Value ==
                "{Binding ToggleFilteredContentVisibilityCommand}");
        Assert.Equal("{Binding FilteredContentToggleText}",
            revealAll.Attribute("Content")?.Value);
        Assert.Contains("original username", Attribute(revealAll, "AutomationProperties.HelpText"));

        string entryViewModel = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "ViewModels", "LiveFeedEntryViewModel.cs"));
        Assert.Contains("IsFiltered && IsFilteredContentRevealed", entryViewModel,
            StringComparison.Ordinal);
        Assert.Contains("_decision.Message.RawText", entryViewModel,
            StringComparison.Ordinal);
        Assert.Contains("if (!IsFiltered)", entryViewModel, StringComparison.Ordinal);

        XElement feedItemStyle = document
            .Descendants(Presentation + "Style")
            .Single(element => element.Attribute(Xaml + "Key")?.Value == "FeedItem");
        Assert.Contains(
            feedItemStyle.Descendants(Presentation + "Setter"),
            setter =>
                setter.Attribute("Property")?.Value == "AutomationProperties.Name" &&
                setter.Attribute("Value")?.Value == "{Binding AccessibleSummary}");
    }

    [Fact]
    public void GlobalShortcutEditor_IsLabeledScreenReaderFriendlyAndReportsStatus()
    {
        XDocument document = LoadMainWindow();
        XElement groupEntry = NamedElement(
            document,
            "Button",
            "GlobalShortcutGroupEntry");
        XElement actionGrid = NamedElement(
            document,
            "ItemsControl",
            "GlobalShortcutGrid");
        XElement navigationGroup = NamedElement(
            document,
            "StackPanel",
            "GlobalShortcutNavigationGroup");
        XElement capturePanel = NamedElement(
            document,
            "Border",
            "InlineShortcutCapturePanel");

        Assert.Equal("17", groupEntry.Attribute("TabIndex")?.Value);
        Assert.Equal("GlobalShortcutGroupEntry_Click", groupEntry.Attribute("Click")?.Value);
        Assert.Contains("Press Enter", Attribute(groupEntry, "AutomationProperties.Name"));
        Assert.Contains("Escape", Attribute(groupEntry, "AutomationProperties.HelpText"));
        Assert.Equal(
            "{Binding GlobalShortcutEditors}",
            actionGrid.Attribute("ItemsSource")?.Value);
        Assert.Equal("Cycle", Attribute(navigationGroup, "KeyboardNavigation.TabNavigation"));
        Assert.Equal("GlobalShortcutNavigationGroup_PreviewKeyDown",
            navigationGroup.Attribute("PreviewKeyDown")?.Value);
        XElement actionButton = actionGrid.Descendants(Presentation + "Button")
            .Single(element => element.Attribute("Tag")?.Value == "GlobalShortcutActionCard");
        Assert.Equal("False", actionButton.Attribute("IsTabStop")?.Value);
        Assert.Equal("GlobalShortcutActionCard_Click", actionButton.Attribute("Click")?.Value);
        Assert.Equal("{Binding CardAutomationName}",
            Attribute(actionButton, "AutomationProperties.Name"));
        Assert.Equal("{Binding CardHelpText}",
            Attribute(actionButton, "AutomationProperties.HelpText"));
        Assert.Equal("Collapsed", capturePanel.Attribute("Visibility")?.Value);
        Assert.DoesNotContain(
            document.Descendants(Presentation + "Button"),
            element => element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml").GetName("Name"))?.Value == "InlineSaveShortcutButton");
        Assert.Contains(
            document.Descendants(),
            element =>
                element.Attribute(XNamespace.Xmlns + "x") is null &&
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName.EndsWith("Announcement", StringComparison.Ordinal) &&
                    attribute.Value.Contains("RelativeSource Self", StringComparison.Ordinal)));
        string window = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml"));
        string shortcuts = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "ViewModels", "MainViewModel.Shortcuts.cs"));
        string codeBehind = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml.cs"));
        Assert.Contains("even when another application has focus", window);
        Assert.Contains("audit logging cannot be assigned to a global shortcut", window);
        Assert.Contains("Tab to Enter keybind group", window);
        Assert.Contains("repeat the same combination to confirm", window);
        Assert.Contains("matching second entry saves automatically", window);
        Assert.DoesNotContain("_announcer.AnnounceFocus(SelectedGlobalShortcutAccessibleText)", shortcuts);
        Assert.Contains("ReservedApplicationShortcuts", shortcuts);
        Assert.Contains("button.IsTabStop = true", codeBehind);
        Assert.Contains("button.IsTabStop = false", codeBehind);
        Assert.Contains("e.Key == Key.System ? e.SystemKey : e.Key", codeBehind);
        Assert.Contains("GlobalShortcutGesture.TryParse", codeBehind);
        Assert.Contains("string.Equals(_firstCapturedShortcut, normalized", codeBehind);
        Assert.Contains("_shortcutTriggerReleased", codeBehind);
        Assert.Contains("remainingModifiers == ModifierKeys.None", codeBehind);
        Assert.Contains("CommitInlineShortcut(normalized, enabled: true)", codeBehind);
        Assert.Contains("Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt", codeBehind);
        Assert.Contains("viewModel.TryApplyGlobalShortcuts()", codeBehind);
        Assert.Contains("_hotkeyService.UnregisterHotkeys()", codeBehind);
        Assert.Contains("ResumeGlobalHotkeysAfterShortcutCapture()", codeBehind);
        Assert.Contains("is already saved for", codeBehind);
        Assert.Contains("could not be saved", codeBehind);
        Assert.Contains("Windows could not activate it", codeBehind);
        Assert.Contains("GlobalShortcutNavigationGroup.IsKeyboardFocusWithin", codeBehind);
        Assert.Contains("DeactivateGlobalShortcutGroup(", codeBehind);
        Assert.Contains("Any unfinished shortcut capture was cancelled", codeBehind);
        Assert.Contains("ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows", codeBehind);
        Assert.Contains("Control plus 1, 2, 3, or 4 always cancels unfinished capture", shortcuts);
        Assert.Contains("TryHandleChapterNavigationShortcut(key)", codeBehind);
        Assert.Contains("Key.D0 or Key.NumPad0 => 10", codeBehind);
        Assert.Contains("EnumerateVisualDescendants(GetSelectedPageContent())", codeBehind);
        Assert.Contains("MainNavigation.SelectedContent is UIElement selectedContent", codeBehind);
        Assert.Contains("AutomationProperties.GetHeadingLevel(label) == AutomationHeadingLevel.Level2", codeBehind);
        Assert.Contains("viewModel?.IsGlobalShortcutConfigured(gesture) == true", codeBehind);
        Assert.Contains("This will override the matching SafeSpeak chapter-navigation shortcut", codeBehind);
        Assert.Contains("Alt+1 through Alt+9", shortcuts);
        Assert.Contains("custom global shortcut using the same Alt-number takes priority", shortcuts);
    }

    [Fact]
    public void SafetyPage_ExposesBoundedCustomAllowedTermsWithoutWeakeningCoreSafety()
    {
        XDocument document = LoadMainWindow();
        XElement input = NamedElement(document, "TextBox", "AllowedTermInput");
        string window = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "MainWindow.xaml"));
        string main = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "ViewModels", "MainViewModel.cs"));

        Assert.Equal(
            "{Binding CustomAllowedInput, UpdateSourceTrigger=PropertyChanged}",
            input.Attribute("Text")?.Value);
        Assert.Contains("Binding CustomAllowedTerms", window);
        Assert.Contains("AddCustomAllowedTermCommand", window);
        Assert.Contains("RemoveSelectedCustomAllowedTermCommand", window);
        Assert.Contains("Built-in severe-abuse rules", window);
        Assert.Contains("_pipeline.Rules.DefaultRules.Contains", main);
        Assert.Contains("Config.CustomAllowedTerms.Add", main);
    }

    [Fact]
    public void SettingsPage_ExposesAdjustableQueueLimitAndFocusablePausedChoices()
    {
        XDocument document = LoadMainWindow();
        XElement queueSlider = NamedElement(document, "Slider", "QueueLimitSlider");
        string[] pauseBindings =
        [
            "AllowGiftAnnouncementsWhilePaused",
            "AllowFollowAnnouncementsWhilePaused",
            "AllowShareAnnouncementsWhilePaused",
            "AllowSubscriptionAnnouncementsWhilePaused"
        ];

        Assert.Equal("1", queueSlider.Attribute("Minimum")?.Value);
        Assert.Equal("500", queueSlider.Attribute("Maximum")?.Value);
        Assert.Equal(
            "{Binding QueueLimit, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
            queueSlider.Attribute("Value")?.Value);

        foreach (string binding in pauseBindings)
        {
            XElement option = document.Descendants(Presentation + "CheckBox")
                .Single(element => element.Attribute("IsChecked")?.Value == $"{{Binding {binding}}}");
            Assert.Null(option.Attribute("IsEnabled"));
            Assert.False(string.IsNullOrWhiteSpace(
                Attribute(option, "AutomationProperties.HelpText")));
        }
    }

    [Fact]
    public void VoiceAndSettingsExposeAccessibleAdaptiveSpacingAndLargeStreamRateLimits()
    {
        XDocument document = LoadMainWindow();
        XElement gap = NamedElement(document, "Slider", "InterMessageGapSlider");
        XElement adaptive = document.Descendants(Presentation + "CheckBox")
            .Single(element => element.Attribute("IsChecked")?.Value ==
                "{Binding AdaptiveInterMessageGap, Mode=TwoWay}");
        XElement window = NamedElement(document, "ComboBox", "MessageRateWindowSelector");
        XElement perUser = NamedElement(document, "Slider", "PerUserMessageLimitSlider");
        XElement stream = NamedElement(document, "Slider", "StreamMessageLimitSlider");

        Assert.Equal("0", gap.Attribute("Minimum")?.Value);
        Assert.Equal("50", gap.Attribute("Maximum")?.Value);
        Assert.Equal("1", gap.Attribute("TickFrequency")?.Value);
        Assert.False(string.IsNullOrWhiteSpace(
            Attribute(gap, "AutomationProperties.HelpText")));
        Assert.False(string.IsNullOrWhiteSpace(
            Attribute(adaptive, "AutomationProperties.HelpText")));

        Assert.Equal("{Binding MessageRateWindowChoices}", window.Attribute("ItemsSource")?.Value);
        Assert.Equal("1", perUser.Attribute("Minimum")?.Value);
        Assert.Equal("100", perUser.Attribute("Maximum")?.Value);
        Assert.Equal("10", stream.Attribute("Minimum")?.Value);
        Assert.Equal("5000", stream.Attribute("Maximum")?.Value);
        Assert.Null(perUser.Attribute("IsEnabled"));
        Assert.Null(stream.Attribute("IsEnabled"));
        Assert.False(string.IsNullOrWhiteSpace(
            Attribute(perUser, "AutomationProperties.HelpText")));
        Assert.False(string.IsNullOrWhiteSpace(
            Attribute(stream, "AutomationProperties.HelpText")));
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

    private static XElement[] ChapterHeadings(XElement tab) =>
        tab.Descendants(Presentation + "Label")
            .Where(element => element.Attribute("Style")?.Value ==
                "{StaticResource ChapterHeading}")
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
