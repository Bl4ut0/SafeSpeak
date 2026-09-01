using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SafeSpeak.App.Contracts.Tests;

public sealed class ThemeAccessibilityContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] ThemeNames =
    [
        "Light",
        "Dark",
        "HighContrast"
    ];

    private static readonly string[] SemanticBrushKeys =
    [
        "SafeSpeakWindowBrush",
        "SafeSpeakSurfaceBrush",
        "SafeSpeakSurfaceAltBrush",
        "SafeSpeakTextBrush",
        "SafeSpeakMutedTextBrush",
        "SafeSpeakBorderBrush",
        "SafeSpeakAccentBrush",
        "SafeSpeakAccentSoftBrush",
        "SafeSpeakAccentTextBrush",
        "SafeSpeakDangerBrush",
        "SafeSpeakDangerTextBrush",
        "SafeSpeakSuccessBrush",
        "SafeSpeakSuccessTextBrush"
    ];

    [Fact]
    public void Themes_ContainExactlyTheThreeNamedPaletteDictionaries()
    {
        string themesDirectory = RepositoryFile("src", "SafeSpeak.App", "Themes");
        string[] actualNames = Directory
            .EnumerateFiles(themesDirectory, "*.xaml", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ThemeNames.Order(StringComparer.Ordinal),
            actualNames);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public void Theme_DefinesEverySemanticBrushExactlyOnce(string themeName)
    {
        XDocument document = LoadTheme(themeName);
        XElement[] brushes = document
            .Descendants(Presentation + "SolidColorBrush")
            .ToArray();
        string[] actualKeys = brushes
            .Select(brush => brush.Attribute(Xaml + "Key")?.Value)
            .Where(key => key is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal(SemanticBrushKeys.Length, brushes.Length);
        Assert.Equal(actualKeys.Length, actualKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            SemanticBrushKeys.Order(StringComparer.Ordinal),
            actualKeys.Order(StringComparer.Ordinal));
        Assert.All(
            brushes,
            brush => Assert.Matches(
                "^#[0-9A-Fa-f]{8}$",
                brush.Attribute("Color")?.Value ?? string.Empty));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public void Theme_TextAndFocusPairsMeetWcagContrast(string themeName)
    {
        IReadOnlyDictionary<string, Rgb> colors = LoadColors(themeName);
        (string foreground, string background, double minimum)[] pairs =
        [
            ("SafeSpeakTextBrush", "SafeSpeakWindowBrush", 4.5),
            ("SafeSpeakTextBrush", "SafeSpeakSurfaceBrush", 4.5),
            ("SafeSpeakMutedTextBrush", "SafeSpeakSurfaceBrush", 4.5),
            ("SafeSpeakAccentTextBrush", "SafeSpeakAccentBrush", 4.5),
            ("SafeSpeakDangerTextBrush", "SafeSpeakDangerBrush", 4.5),
            ("SafeSpeakSuccessTextBrush", "SafeSpeakSuccessBrush", 4.5),
            ("SafeSpeakAccentBrush", "SafeSpeakWindowBrush", 3.0),
            ("SafeSpeakAccentBrush", "SafeSpeakSurfaceBrush", 3.0)
        ];

        foreach ((string foreground, string background, double minimum) in pairs)
        {
            double ratio = Contrast(colors[foreground], colors[background]);
            Assert.True(
                ratio >= minimum,
                $"{themeName}: {foreground} on {background} is {ratio:F2}:1; " +
                $"expected at least {minimum:F1}:1.");
        }
    }

    [Fact]
    public void Application_UsesDynamicResourcesForEverySemanticBrushReference()
    {
        string appDirectory = RepositoryFile("src", "SafeSpeak.App");
        string[] xamlFiles = Directory
            .EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories)
            .ToArray();
        var referencePattern = new Regex(
            @"\{(?<kind>[^}\s]+)[^}]*\bSafeSpeak[A-Za-z]+Brush\b[^}]*\}",
            RegexOptions.CultureInvariant);
        var violations = new List<string>();
        int referenceCount = 0;

        foreach (string file in xamlFiles)
        {
            string text = File.ReadAllText(file);
            foreach (Match match in referencePattern.Matches(text))
            {
                referenceCount++;
                if (!string.Equals(
                        match.Groups["kind"].Value,
                        "DynamicResource",
                        StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{Path.GetRelativePath(appDirectory, file)}: {match.Value}");
                }
            }
        }

        Assert.True(referenceCount > 0, "No semantic theme references were found.");
        Assert.True(
            violations.Count == 0,
            "Semantic brushes must update at runtime via DynamicResource: " +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Application_DefinesAThreeDipKeyboardFocusIndicator()
    {
        XDocument document = XDocument.Load(
            RepositoryFile("src", "SafeSpeak.App", "App.xaml"));
        XElement focusStyle = document
            .Descendants(Presentation + "Style")
            .Single(style =>
                style.Attribute(Xaml + "Key")?.Value == "SafeSpeakFocusVisual");
        XElement rectangle = focusStyle
            .Descendants(Presentation + "Rectangle")
            .Single();

        Assert.Equal("3", rectangle.Attribute("StrokeThickness")?.Value);
        Assert.Equal(
            "{DynamicResource SafeSpeakAccentBrush}",
            rectangle.Attribute("Stroke")?.Value);

        string[] focusableTypes =
        [
            "Button",
            "TextBox",
            "ComboBox",
            "CheckBox",
            "Slider",
            "TabItem",
            "ListView"
        ];
        foreach (string controlType in focusableTypes)
        {
            XElement style = document
                .Descendants(Presentation + "Style")
                .First(candidate => candidate.Attribute("TargetType")?.Value == controlType);
            Assert.Contains(
                style.Elements(Presentation + "Setter"),
                setter =>
                    setter.Attribute("Property")?.Value == "FocusVisualStyle" &&
                    setter.Attribute("Value")?.Value ==
                        "{StaticResource SafeSpeakFocusVisual}");
        }
    }

    [Fact]
    public void Startup_RestoresTheEffectiveSavedTheme()
    {
        string startup = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "App.xaml.cs"));
        int load = startup.IndexOf("var settings = AppSettings.Load();", StringComparison.Ordinal);
        int apply = startup.IndexOf(
            "ThemeManager.Apply(settings.EffectiveTheme);",
            StringComparison.Ordinal);

        Assert.True(load >= 0, "Startup must load persisted settings.");
        Assert.True(apply > load, "Startup must apply the effective saved theme after loading settings.");
        Assert.DoesNotContain("ThemeManager.Apply(settings.Theme);", startup);
    }

    [Fact]
    public void ThemeManager_OverridesForWindowsHighContrastAndRestoresRequestedTheme()
    {
        string manager = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "ThemeManager.cs"));
        string startup = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "App.xaml.cs"));

        Assert.Contains(
            "public static ThemePreference RequestedTheme { get; private set; }",
            manager);
        Assert.Contains("RequestedTheme = Normalize(theme);", manager);
        Assert.Contains(
            "SystemParameters.HighContrast ? ThemePreference.HighContrast : RequestedTheme",
            manager);
        Assert.Matches(
            @"SystemParameters\.HighContrast\s*\?\s*CreateWindowsHighContrastPalette\(\)\s*:\s*LoadApplicationPalette\(RequestedTheme\)",
            manager);
        Assert.Contains(
            "public static void RefreshForSystemSettings() => ApplyEffectivePalette();",
            manager);
        Assert.Contains(
            "args.PropertyName == nameof(SystemParameters.HighContrast)",
            startup);
        Assert.Contains("ThemeManager.RefreshForSystemSettings();", startup);

        foreach (string key in SemanticBrushKeys)
        {
            Assert.Contains($"[\"{key}\"]", manager);
        }
    }

    [Theory]
    [InlineData("ThemePreference.Dark", "Dark.xaml")]
    [InlineData("ThemePreference.HighContrast", "HighContrast.xaml")]
    [InlineData("_", "Light.xaml")]
    public void ThemeManager_MapsEachPreferenceToItsExactDictionary(
        string preference,
        string dictionary)
    {
        string manager = File.ReadAllText(
            RepositoryFile("src", "SafeSpeak.App", "ThemeManager.cs"));

        Assert.Matches(
            Regex.Escape(preference) +
            "\\s*=>\\s*\\\"" +
            Regex.Escape(dictionary) +
            "\\\"",
            manager);
    }

    private static XDocument LoadTheme(string themeName) =>
        XDocument.Load(
            RepositoryFile(
                "src",
                "SafeSpeak.App",
                "Themes",
                $"{themeName}.xaml"),
            LoadOptions.SetLineInfo);

    private static IReadOnlyDictionary<string, Rgb> LoadColors(string themeName) =>
        LoadTheme(themeName)
            .Descendants(Presentation + "SolidColorBrush")
            .ToDictionary(
                brush => brush.Attribute(Xaml + "Key")!.Value,
                brush => ParseColor(brush.Attribute("Color")!.Value),
                StringComparer.Ordinal);

    private static Rgb ParseColor(string color)
    {
        Assert.Matches("^#[0-9A-Fa-f]{8}$", color);
        return new Rgb(
            byte.Parse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(color.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static double Contrast(Rgb first, Rgb second)
    {
        double lighter = Math.Max(Luminance(first), Luminance(second));
        double darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Rgb color) =>
        0.2126 * Linearize(color.Red) +
        0.7152 * Linearize(color.Green) +
        0.0722 * Linearize(color.Blue);

    private static double Linearize(byte component)
    {
        double channel = component / 255d;
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
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

    private readonly record struct Rgb(byte Red, byte Green, byte Blue);
}
