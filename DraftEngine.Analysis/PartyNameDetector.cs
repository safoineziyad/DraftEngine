using System.Text.RegularExpressions;

namespace DraftEngine.Analysis;

/// <summary>
/// Finds likely party names in a document so the existing redaction service can hide them
/// before an AI review. Deliberately over-inclusive: hiding an extra company name is safe.
/// </summary>
public static class PartyNameDetector
{
    private static readonly Regex EntitySuffix = new(
        @"\b([A-Z][A-Za-z0-9&.,'’\-]*(?:\s+[A-Z][A-Za-z0-9&.,'’\-]*){0,6}\s+(?:Inc\.?|LLC|L\.L\.C\.|Ltd\.?|Limited|Corp\.?|Corporation|GmbH|S\.A\.|SARL|PLC))\b",
        RegexOptions.Compiled);

    private static readonly Regex BetweenAnd = new(
        @"\bbetween\s+([A-Z][^,\n]{2,70}?)\s+and\s+([A-Z][^,\n.]{2,70}?)(?:\s*[.,(]|\n|$)",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in EntitySuffix.Matches(text))
        {
            Add(names, match.Groups[1].Value);
        }

        foreach (Match match in BetweenAnd.Matches(text))
        {
            Add(names, match.Groups[1].Value);
            Add(names, match.Groups[2].Value);
        }

        return names.OrderByDescending(name => name.Length).ToList();
    }

    private static void Add(HashSet<string> names, string value)
    {
        var trimmed = value.Trim().TrimEnd(',', ';');
        if (trimmed.Length >= 3 && trimmed.Any(char.IsLetter))
        {
            names.Add(trimmed);
        }
    }
}
