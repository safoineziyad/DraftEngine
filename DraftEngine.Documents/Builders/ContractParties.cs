using System.Text.Json;

namespace DraftEngine.Documents.Builders;

/// <summary>
/// Converts a contract's PartiesJson into readable display text and party names.
/// </summary>
internal static class ContractParties
{
    /// <summary>
    /// Returns a human-readable parties string ("Acme Corp and Globex Ltd"), or the raw
    /// JSON/text when it cannot be parsed, or "N/A" when empty.
    /// </summary>
    internal static string ToDisplayText(string? partiesJson)
    {
        var names = GetNames(partiesJson);
        if (names.Count > 0)
        {
            return string.Join(" and ", names);
        }

        return string.IsNullOrWhiteSpace(partiesJson) ? "N/A" : partiesJson.Trim();
    }

    /// <summary>
    /// Extracts party names from PartiesJson (array of objects with a "name" property,
    /// or an array of strings). Returns an empty list when nothing can be extracted.
    /// </summary>
    internal static IReadOnlyList<string> GetNames(string? partiesJson)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(partiesJson))
        {
            return names;
        }

        try
        {
            using var json = JsonDocument.Parse(partiesJson);
            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                return names;
            }

            foreach (var element in json.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        names.Add(value.Trim());
                    }

                    continue;
                }

                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                            property.Value.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(property.Value.GetString()))
                        {
                            names.Add(property.Value.GetString()!.Trim());
                            break;
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — the caller falls back to the raw text.
        }

        return names;
    }
}
