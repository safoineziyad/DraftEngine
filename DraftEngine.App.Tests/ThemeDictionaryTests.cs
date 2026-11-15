using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Guards the theme dictionaries: Light and Dark expose exactly the same keys, declared
/// text/background pairs meet WCAG AA (4.5:1), every brush referenced from XAML exists in
/// both themes, and views/styles contain no raw color literals.
/// </summary>
public sealed class ThemeDictionaryTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string Base = AppContext.BaseDirectory;

    [Fact]
    public void LightAndDark_HaveIdenticalKeys()
    {
        var light = LoadKeys("Light.xaml");
        var dark = LoadKeys("Dark.xaml");

        Assert.NotEmpty(light);
        Assert.Equal(light.OrderBy(k => k, StringComparer.Ordinal), dark.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Light.xaml")]
    [InlineData("Dark.xaml")]
    public void DeclaredTextBackgroundPairs_MeetWcagAa(string file)
    {
        var colors = LoadColors(file);

        (string Text, string Background)[] pairs =
        [
            ("TextPrimaryBrush", "WindowBackgroundBrush"),
            ("TextPrimaryBrush", "SurfaceBrush"),
            ("TextSecondaryBrush", "WindowBackgroundBrush"),
            ("TextSecondaryBrush", "PanelBrush"),
            ("TextSecondaryBrush", "SurfaceBrush"),
            ("AccentForegroundBrush", "AccentBrush"),
            ("AccentMutedForegroundBrush", "AccentBrush"),
            ("DangerForegroundBrush", "DangerBrush"),
            ("SuccessForegroundBrush", "SuccessBrush"),
            ("SelectedForegroundBrush", "SelectedBackgroundBrush"),
            ("TabSelectedForegroundBrush", "SurfaceBrush"),
            ("TooltipForegroundBrush", "TooltipBackgroundBrush"),
            ("WarningForegroundBrush", "WarningBackgroundBrush"),
            ("DiffAddedForegroundBrush", "DiffAddedBrush"),
            ("DiffRemovedForegroundBrush", "DiffRemovedBrush"),
        ];

        foreach (var (text, background) in pairs)
        {
            Assert.True(colors.ContainsKey(text), $"{file} is missing '{text}'.");
            Assert.True(colors.ContainsKey(background), $"{file} is missing '{background}'.");
            var ratio = ContrastRatio(colors[text], colors[background]);
            Assert.True(ratio >= 4.5,
                $"{file}: {text} on {background} has contrast {ratio.ToString("F2", CultureInfo.InvariantCulture)}:1 (< 4.5:1).");
        }
    }

    [Fact]
    public void EveryBrushReferencedFromXaml_ExistsInBothThemes()
    {
        var light = LoadKeys("Light.xaml");
        var dark = LoadKeys("Dark.xaml");
        var referenced = XamlFiles()
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"\{DynamicResource\s+([A-Za-z0-9_.]+)\}"))
            .Select(match => match.Groups[1].Value)
            .Where(key => key.EndsWith("Brush", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(referenced);
        foreach (var key in referenced)
        {
            Assert.True(light.Contains(key), $"Light.xaml is missing referenced brush '{key}'.");
            Assert.True(dark.Contains(key), $"Dark.xaml is missing referenced brush '{key}'.");
        }
    }

    [Fact]
    public void HighContrast_DefinesEveryReferencedBrush()
    {
        var highContrast = LoadKeys("HighContrast.xaml");
        var referenced = XamlFiles()
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"\{DynamicResource\s+([A-Za-z0-9_.]+)\}"))
            .Select(match => match.Groups[1].Value)
            .Where(key => key.EndsWith("Brush", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);

        foreach (var key in referenced)
        {
            Assert.True(highContrast.Contains(key), $"HighContrast.xaml is missing referenced brush '{key}'.");
        }
    }

    [Fact]
    public void ViewsAndStyles_ContainNoColorLiterals()
    {
        var offenders = new List<string>();
        foreach (var file in XamlFiles())
        {
            var text = File.ReadAllText(file);
            var matches = Regex.Matches(
                text,
                @"#[0-9A-Fa-f]{3,8}|\x22(?:White|Black|Gray|Grey|Red|Green|Blue|Yellow|Orange)\x22");
            if (matches.Count > 0)
            {
                offenders.Add($"{Path.GetFileName(file)} ({matches.Count})");
            }
        }

        Assert.Empty(offenders);
    }

    private static HashSet<string> LoadKeys(string themeFile)
    {
        var document = XDocument.Load(Path.Combine(Base, "Themes", themeFile));
        return document.Root!.Elements()
            .Select(element => element.Attribute(X + "Key")?.Value)
            .Where(key => key is not null)
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, Color> LoadColors(string themeFile)
    {
        var document = XDocument.Load(Path.Combine(Base, "Themes", themeFile));

        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (var color in document.Root!.Elements().Where(e => e.Name.LocalName == "Color"))
        {
            var key = color.Attribute(X + "Key")?.Value;
            if (key is not null && TryParse(color.Value, out var parsed))
            {
                colors[key] = parsed;
            }
        }

        var brushes = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (var brush in document.Root!.Elements().Where(e => e.Name.LocalName == "SolidColorBrush"))
        {
            var key = brush.Attribute(X + "Key")?.Value;
            var value = brush.Attribute("Color")?.Value;
            if (key is null || value is null)
            {
                continue;
            }

            var referenced = Regex.Match(value, @"\{StaticResource\s+([A-Za-z0-9_.]+)\}");
            if (referenced.Success && colors.TryGetValue(referenced.Groups[1].Value, out var aliased))
            {
                brushes[key] = aliased;
            }
            else if (TryParse(value, out var parsed))
            {
                brushes[key] = parsed;
            }
        }

        foreach (var (key, value) in colors)
        {
            brushes.TryAdd(key, value);
        }

        return brushes;
    }

    private static bool TryParse(string value, out Color color)
    {
        color = default;
        if (!value.StartsWith('#') || (value.Length != 7 && value.Length != 9))
        {
            return false;
        }

        if (ColorConverter.ConvertFromString(value) is Color parsed)
        {
            color = parsed;
            return true;
        }

        return false;
    }

    private static double ContrastRatio(Color first, Color second)
    {
        var lighter = Math.Max(Luminance(first), Luminance(second));
        var darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var s = value / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static IEnumerable<string> XamlFiles()
    {
        yield return Path.Combine(Base, "App.xaml");
        foreach (var file in Directory.GetFiles(Path.Combine(Base, "Xaml"), "*.xaml", SearchOption.AllDirectories))
        {
            yield return file;
        }

        yield return Path.Combine(Base, "Themes", "Styles.xaml");
    }
}
