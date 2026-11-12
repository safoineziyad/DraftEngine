using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Guards the localization assets: every language has the same keys, no value is empty,
/// every key referenced from XAML exists, and migrated screens contain no literal
/// user-facing strings. The pending list shrinks as each screen is migrated in Step 3.
/// </summary>
public sealed class LocalizationResourceTests
{
    private static readonly string ResourcesDirectory =
        Path.Combine(AppContext.BaseDirectory, "Resources");

    private static readonly string XamlDirectory =
        Path.Combine(AppContext.BaseDirectory, "Xaml");

    private static readonly string LiteralBaselinePath =
        Path.Combine(AppContext.BaseDirectory, "i18n-baseline.txt");

    private static readonly string[] LanguageFiles =
    [
        "Strings.resx",
        "Strings.fr.resx",
        "Strings.ar.resx",
        "Strings.es.resx"
    ];

    // Screens not yet migrated to {loc:T}. Remove a name when its migration commit lands.
    private static readonly HashSet<string> PendingMigration = new(StringComparer.OrdinalIgnoreCase)
    {
        "ClauseManagerView.xaml",
        "AnalyzeView.xaml",
        "AboutView.xaml",
        "LicenseActivationWindow.xaml"
    };

    // Brand/product names are intentionally not translated.
    private static readonly HashSet<string> BrandNames = new(StringComparer.Ordinal)
    {
        "DraftEngine"
    };

    [Fact]
    public void AllLanguages_HaveTheSameKeys()
    {
        var resources = LoadResources();
        var referenceKeys = resources["Strings.resx"].Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(referenceKeys);

        foreach (var file in LanguageFiles)
        {
            var keys = resources[file].Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            Assert.Equal(referenceKeys, keys);
        }
    }

    [Fact]
    public void NoTranslationValue_IsEmpty()
    {
        foreach (var (file, values) in LoadResources())
        {
            foreach (var (key, value) in values)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{file} key '{key}' is empty.");
            }
        }
    }

    [Fact]
    public void EveryLocMarkupKey_ExistsInEveryLanguage()
    {
        var resources = LoadResources();
        var referenced = XamlFiles()
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"\{loc:T\s+([A-Za-z0-9_.]+)"))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(referenced);

        foreach (var key in referenced)
        {
            foreach (var file in LanguageFiles)
            {
                Assert.True(resources[file].ContainsKey(key), $"{file} is missing XAML key '{key}'.");
            }
        }
    }

    [Fact]
    public void MigratedScreens_ContainNoLiteralUserFacingStrings()
    {
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            if (HasLiteralUserFacingString(File.ReadAllText(file)))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        var unexpected = offenders
            .Where(file => !PendingMigration.Contains(file))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpected);
    }

    [Fact]
    public void PendingLiteralCount_DoesNotExceedBaseline()
    {
        Assert.True(File.Exists(LiteralBaselinePath), $"Expected literal baseline at '{LiteralBaselinePath}'.");
        var baseline = int.Parse(
            File.ReadAllText(LiteralBaselinePath).Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
        var actual = XamlFiles()
            .Where(file => PendingMigration.Contains(Path.GetFileName(file)))
            .Sum(file => CountLiteralUserFacingStrings(File.ReadAllText(file)));

        Assert.True(actual <= baseline, $"Pending literal count {actual} exceeds baseline {baseline}.");
    }

    [Fact]
    public void MainWindow_HasNoLiteralUserFacingStrings()
    {
        var path = Path.Combine(XamlDirectory, "MainWindow.xaml");
        Assert.True(File.Exists(path), $"Expected linked XAML at '{path}'.");
        Assert.False(HasLiteralUserFacingString(File.ReadAllText(path)));
    }

    private static bool HasLiteralUserFacingString(string xaml)
        => CountLiteralUserFacingStrings(xaml) > 0;

    private static int CountLiteralUserFacingStrings(string xaml)
    {
        const string attributePattern =
            @"(?:Text|Content|Header|ToolTip|Title|Watermark|PlaceholderText|AutomationProperties\.Name)\s*=\s*""([^""]*)""";
        var count = 0;

        foreach (Match match in Regex.Matches(xaml, attributePattern))
        {
            var value = match.Groups[1].Value.Trim();
            // The required-field asterisk is a symbol, not translatable copy.
            if (value.Length == 0 || value.StartsWith('{') || value == "*" || BrandNames.Contains(value))
            {
                continue;
            }

            if (Regex.IsMatch(value, "^[0-9.,]+$"))
            {
                continue;
            }

            count++;
        }

        // Inline text such as <TextBlock>Hello</TextBlock> (single line only).
        count += Regex.Matches(xaml, @">\s*([A-Za-z][^<>{}]{2,})\s*</(?:TextBlock|Run|Hyperlink)>").Count;
        return count;
    }

    private static IEnumerable<string> XamlFiles()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "App.xaml");

        foreach (var file in Directory.GetFiles(XamlDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            yield return file;
        }
    }

    private static Dictionary<string, Dictionary<string, string>> LoadResources()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var file in LanguageFiles)
        {
            var path = Path.Combine(ResourcesDirectory, file);
            Assert.True(File.Exists(path), $"Expected linked resource at '{path}'.");

            var document = XDocument.Load(path);
            result[file] = document.Root!
                .Elements("data")
                .Where(element => element.Attribute("name") is not null)
                .ToDictionary(
                    element => element.Attribute("name")!.Value,
                    element => element.Element("value")?.Value ?? string.Empty,
                    StringComparer.Ordinal);
        }

        return result;
    }
}
