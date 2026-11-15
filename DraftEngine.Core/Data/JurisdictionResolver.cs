using System;
using System.Collections.Generic;
using System.Linq;

namespace DraftEngine.Core.Data;

/// <summary>A jurisdiction the user picked: ISO country, optional subdivision and display text.</summary>
public sealed record Jurisdiction(string CountryCode, string Subdivision)
{
    /// <summary>The human-readable "Country — Subdivision" noun phrase.</summary>
    public string Display(string language) => JurisdictionResolver.Compose(CountryCode, Subdivision, language);
}

/// <summary>
/// Converts between legacy free-text governing-law values and the structured
/// country/subdivision pair. Mapping never discards the original text: unknown values return
/// false and callers keep the old string.
/// </summary>
public static class JurisdictionResolver
{
    private static readonly Dictionary<string, string> CountryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["United States"] = "US",
        ["United States of America"] = "US",
        ["USA"] = "US",
        ["US"] = "US",
        ["United Kingdom"] = "GB",
        ["UK"] = "GB",
        ["Great Britain"] = "GB",
        ["England"] = "GB",
        ["England and Wales"] = "GB",
        ["Scotland"] = "GB",
        ["Wales"] = "GB",
        ["Northern Ireland"] = "GB"
    };

    /// <summary>Builds the display noun phrase, e.g. "United States — California".</summary>
    public static string Compose(string? countryCode, string? subdivision, string language)
    {
        var country = CountryCatalog.Find(countryCode);
        var countryName = country?.Name(language) ?? (countryCode ?? string.Empty).Trim();
        var region = (subdivision ?? string.Empty).Trim();

        if (countryName.Length == 0)
        {
            return region;
        }

        return region.Length == 0 ? countryName : $"{countryName} — {region}";
    }

    /// <summary>
    /// Maps a legacy free-text governing law such as "California" or "England and Wales".
    /// Returns false (and empty values) when nothing certain can be inferred.
    /// </summary>
    public static bool TryMapLegacy(string? legacy, out string countryCode, out string subdivision)
    {
        countryCode = string.Empty;
        subdivision = string.Empty;
        if (string.IsNullOrWhiteSpace(legacy))
        {
            return false;
        }

        var value = CountryCatalog.Normalize(legacy);

        foreach (var candidate in new[] { "US", "CA", "AU", "GB" })
        {
            var match = SubdivisionCatalog.For(candidate)
                .FirstOrDefault(name => CountryCatalog.Normalize(name) == value);
            if (match is not null)
            {
                countryCode = candidate;
                subdivision = match;
                return true;
            }
        }

        if (CountryAliases.TryGetValue(legacy.Trim(), out var alias))
        {
            countryCode = alias;
            // "England and Wales" is retained verbatim: no built-in list contains it.
            subdivision = alias == "GB" && value == "england and wales" ? "England and Wales" : string.Empty;
            return true;
        }

        foreach (var country in CountryCatalog.All)
        {
            foreach (var language in new[] { "en", "fr", "ar", "es" })
            {
                if (CountryCatalog.Normalize(country.Name(language)) == value)
                {
                    countryCode = country.Code;
                    subdivision = string.Empty;
                    return true;
                }
            }
        }

        return false;
    }
}
