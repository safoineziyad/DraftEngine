using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace DraftEngine.Core.Data;

/// <summary>One ISO 3166-1 country or territory with CLDR names in the supported languages.</summary>
public sealed class CountryInfo
{
    /// <summary>Creates a country entry.</summary>
    public CountryInfo(string code, IReadOnlyDictionary<string, string> names)
    {
        Code = code;
        Names = names;
    }

    /// <summary>ISO 3166-1 alpha-2 code.</summary>
    public string Code { get; }

    /// <summary>Localized names keyed by language code (en, fr, ar, es).</summary>
    public IReadOnlyDictionary<string, string> Names { get; }

    /// <summary>The name in the requested language, falling back to English.</summary>
    public string Name(string language) =>
        Names.TryGetValue(NormalizeLanguage(language), out var value) ? value : Names["en"];

    /// <summary>The name for the current UI culture, used by WPF list bindings.</summary>
    public string DisplayName => Name(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    internal static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "en";
        }

        var code = language.Split('-', '_')[0].ToLowerInvariant();
        return code is "fr" or "ar" or "es" or "en" ? code : "en";
    }
}

/// <summary>
/// Offline country catalog bundled from the Unicode CLDR territory data (Unicode License v3).
/// No network call is made at runtime. Sorting and search use the active culture's collation,
/// including Arabic, and ignore diacritics so "Cote" finds "Côte d’Ivoire".
/// </summary>
public static class CountryCatalog
{
    private static readonly Lazy<IReadOnlyList<CountryInfo>> Countries = new(Load);

    /// <summary>Every bundled country/territory, sorted by English name.</summary>
    public static IReadOnlyList<CountryInfo> All => Countries.Value;

    /// <summary>Returns the entry for an ISO code, or null when it is not bundled.</summary>
    public static CountryInfo? Find(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(country =>
                string.Equals(country.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The localized name for a code, or the code itself when unknown.</summary>
    public static string Name(string? code, string language) =>
        Find(code)?.Name(language) ?? (code ?? string.Empty);

    /// <summary>
    /// The country suggested by the Windows region, falling back to the United States when the
    /// region is unknown or missing from the catalog.
    /// </summary>
    public static string DefaultCode()
    {
        try
        {
            var region = RegionInfo.CurrentRegion.TwoLetterISORegionName;
            if (Find(region) is not null)
            {
                return region;
            }
        }
        catch (ArgumentException)
        {
            // No usable region; fall through.
        }

        return "US";
    }

    /// <summary>Sorts countries by the localized name using the culture's collation.</summary>
    public static IReadOnlyList<CountryInfo> Sort(IEnumerable<CountryInfo> countries, CultureInfo? culture)
    {
        var compare = (culture ?? CultureInfo.CurrentCulture).CompareInfo;
        return countries
            .OrderBy(country => country.Name(LanguageFor(culture)), CountryNameComparer.Instance(compare))
            .ToList();
    }

    /// <summary>
    /// Type-ahead search over codes and localized names. Matching ignores case and diacritics;
    /// a country matches when its name starts with the query or a word in the name does.
    /// </summary>
    public static IReadOnlyList<CountryInfo> Search(string? query, CultureInfo? culture)
    {
        var language = LanguageFor(culture);
        var sorted = Sort(All, culture);
        if (string.IsNullOrWhiteSpace(query))
        {
            return sorted;
        }

        var needle = Normalize(query);
        return sorted
            .Where(country =>
                Normalize(country.Code).StartsWith(needle, StringComparison.Ordinal) ||
                Normalize(country.Name(language)).StartsWith(needle, StringComparison.Ordinal) ||
                Normalize(country.Name(language)).Split(' ', '’', '\'', '-', '.')
                    .Any(word => word.StartsWith(needle, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>Maps a culture to one of the four bundled languages.</summary>
    public static string LanguageFor(CultureInfo? culture) =>
        CountryInfo.NormalizeLanguage(culture?.TwoLetterISOLanguageName);

    /// <summary>Removes diacritics and case so search works across keyboard layouts.</summary>
    internal static string Normalize(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static IReadOnlyList<CountryInfo> Load()
    {
        var assembly = typeof(CountryCatalog).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream("DraftEngine.Core.Data.countries.json")
            ?? throw new InvalidOperationException("The bundled country list is missing.");
        using var reader = new StreamReader(stream);
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(reader.ReadToEnd())
            ?? new List<Dictionary<string, string>>();

        return rows
            .Where(row => row.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code))
            .Select(row => new CountryInfo(
                row["code"],
                row.Where(pair => pair.Key != "code")
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    private sealed class CountryNameComparer : IComparer<string>
    {
        private readonly CompareInfo _compare;

        private CountryNameComparer(CompareInfo compare) => _compare = compare;

        public static CountryNameComparer Instance(CompareInfo compare) => new(compare);

        public int Compare(string? x, string? y) =>
            _compare.Compare(x ?? string.Empty, y ?? string.Empty, CompareOptions.IgnoreCase);
    }
}
