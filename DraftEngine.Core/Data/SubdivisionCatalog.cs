using System;
using System.Collections.Generic;
using System.Linq;

namespace DraftEngine.Core.Data;

/// <summary>
/// Subdivisions offered for the countries where the list is certain: the US states and DC,
/// Canadian provinces and territories, Australian states and territories, and the four UK
/// nations. Every other country uses free text, because a wrong subdivision list is worse
/// than none.
/// </summary>
public static class SubdivisionCatalog
{
    private static readonly string[] UsStates =
    [
        "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut",
        "Delaware", "District of Columbia", "Florida", "Georgia", "Hawaii", "Idaho", "Illinois",
        "Indiana", "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland",
        "Massachusetts", "Michigan", "Minnesota", "Mississippi", "Missouri", "Montana",
        "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico", "New York",
        "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania",
        "Rhode Island", "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah",
        "Vermont", "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming"
    ];

    private static readonly string[] CanadianProvinces =
    [
        "Alberta", "British Columbia", "Manitoba", "New Brunswick", "Newfoundland and Labrador",
        "Northwest Territories", "Nova Scotia", "Nunavut", "Ontario", "Prince Edward Island",
        "Quebec", "Saskatchewan", "Yukon"
    ];

    private static readonly string[] AustralianStates =
    [
        "Australian Capital Territory", "New South Wales", "Northern Territory", "Queensland",
        "South Australia", "Tasmania", "Victoria", "Western Australia"
    ];

    private static readonly string[] UkNations =
    [
        "England", "Northern Ireland", "Scotland", "Wales"
    ];

    /// <summary>True when a built-in subdivision list exists for the country.</summary>
    public static bool HasList(string? countryCode) => For(countryCode).Count > 0;

    /// <summary>The built-in subdivisions, or an empty list when free text must be used.</summary>
    public static IReadOnlyList<string> For(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return Array.Empty<string>();
        }

        return countryCode.Trim().ToUpperInvariant() switch
        {
            "US" => UsStates,
            "CA" => CanadianProvinces,
            "AU" => AustralianStates,
            "GB" => UkNations,
            _ => Array.Empty<string>()
        };
    }

    /// <summary>Finds a subdivision case-insensitively, returning the canonical spelling.</summary>
    public static string? Canonical(string? countryCode, string? subdivision)
    {
        if (string.IsNullOrWhiteSpace(subdivision))
        {
            return null;
        }

        return For(countryCode).FirstOrDefault(candidate =>
            string.Equals(candidate, subdivision.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
