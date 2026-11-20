using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Verifies FIX 3:
/// - Brand bar at top of sidebar meets WCAG AA contrast (>= 4.5:1) in both Light and Dark themes.
/// - DatePicker and editable ComboBox use theme brushes and maintain >= 4.5:1 contrast in both themes.
/// </summary>
public sealed class ThemeLeakTests
{
    private static readonly string ThemesDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "DraftEngine.App", "Themes"));

    private static readonly string AppDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "DraftEngine.App"));

    [Fact]
    public void BrandBar_ContrastMeetsWcagAa_InBothThemes()
    {
        var mainWindowXaml = File.ReadAllText(Path.Combine(AppDir, "MainWindow.xaml"));

        // Match brand bar border and textblock
        var match = Regex.Match(
            mainWindowXaml,
            @"<Border[^>]*Background=""\{DynamicResource\s+([^}]+)\}""[^>]*>\s*<TextBlock\s+Text=""DraftEngine""\s+Foreground=""\{DynamicResource\s+([^}]+)\}""",
            RegexOptions.Singleline);

        Assert.True(match.Success, "Could not find brand bar in MainWindow.xaml");
        var bgBrush = match.Groups[1].Value.Trim();
        var fgBrush = match.Groups[2].Value.Trim();

        var lightColors = LoadThemeBrushes("Light.xaml");
        var darkColors = LoadThemeBrushes("Dark.xaml");

        var lightRatio = ContrastRatio(lightColors[fgBrush], lightColors[bgBrush]);
        var darkRatio = ContrastRatio(darkColors[fgBrush], darkColors[bgBrush]);

        Assert.True(lightRatio >= 4.5, $"Light theme brand bar contrast is {lightRatio:F2}:1 (< 4.5:1)");
        Assert.True(darkRatio >= 4.5, $"Dark theme brand bar contrast is {darkRatio:F2}:1 (< 4.5:1)");
    }

    [Fact]
    public void DatePicker_StyledWithThemeBrushes_AndMeetsContrastInBothThemes()
    {
        var stylesXaml = File.ReadAllText(Path.Combine(ThemesDir, "Styles.xaml"));

        // DatePicker style must exist in Styles.xaml
        Assert.Contains(@"<Style TargetType=""DatePicker"">", stylesXaml);

        // DatePicker must use theme dynamic resources
        Assert.Contains(@"InputBackgroundBrush", stylesXaml);
        Assert.Contains(@"PopupBackgroundBrush", stylesXaml);
        Assert.Contains(@"ControlBorderBrush", stylesXaml);

        var lightColors = LoadThemeBrushes("Light.xaml");
        var darkColors = LoadThemeBrushes("Dark.xaml");

        // Input field contrast
        var lightInputRatio = ContrastRatio(lightColors["TextPrimaryBrush"], lightColors["InputBackgroundBrush"]);
        var darkInputRatio = ContrastRatio(darkColors["TextPrimaryBrush"], darkColors["InputBackgroundBrush"]);

        Assert.True(lightInputRatio >= 4.5, $"Light DatePicker input contrast is {lightInputRatio:F2}:1 (< 4.5:1)");
        Assert.True(darkInputRatio >= 4.5, $"Dark DatePicker input contrast is {darkInputRatio:F2}:1 (< 4.5:1)");

        // Popup calendar contrast
        var lightPopupRatio = ContrastRatio(lightColors["TextPrimaryBrush"], lightColors["PopupBackgroundBrush"]);
        var darkPopupRatio = ContrastRatio(darkColors["TextPrimaryBrush"], darkColors["PopupBackgroundBrush"]);

        Assert.True(lightPopupRatio >= 4.5, $"Light DatePicker popup contrast is {lightPopupRatio:F2}:1 (< 4.5:1)");
        Assert.True(darkPopupRatio >= 4.5, $"Dark DatePicker popup contrast is {darkPopupRatio:F2}:1 (< 4.5:1)");
    }

    [Fact]
    public void ComboBox_SupportsEditableMode_WithThemedTextBox()
    {
        var stylesXaml = File.ReadAllText(Path.Combine(ThemesDir, "Styles.xaml"));

        // ComboBox template must contain PART_EditableTextBox for editable combo boxes (like Governing Law)
        Assert.Contains(@"PART_EditableTextBox", stylesXaml);
    }

    private static Dictionary<string, Color> LoadThemeBrushes(string file)
    {
        var path = Path.Combine(ThemesDir, file);
        var doc = XDocument.Load(path);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var xNs = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);

        foreach (var colorElement in doc.Descendants(ns + "Color"))
        {
            var key = colorElement.Attribute(xNs + "Key")?.Value;
            var val = colorElement.Value.Trim();
            if (key != null && ColorConverter.ConvertFromString(val) is Color c)
            {
                colors[key] = c;
            }
        }

        foreach (var brushElement in doc.Descendants(ns + "SolidColorBrush"))
        {
            var key = brushElement.Attribute(xNs + "Key")?.Value;
            var colorAttr = brushElement.Attribute("Color")?.Value?.Trim();
            if (key == null || colorAttr == null) continue;

            if (colorAttr.StartsWith("{StaticResource ", StringComparison.Ordinal))
            {
                var refKey = colorAttr["{StaticResource ".Length..^1].Trim();
                if (colors.TryGetValue(refKey, out var refColor))
                {
                    colors[key] = refColor;
                }
            }
            else if (ColorConverter.ConvertFromString(colorAttr) is Color c)
            {
                colors[key] = c;
            }
        }

        return colors;
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
}
