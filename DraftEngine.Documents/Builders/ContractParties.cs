using System.Text.Json;
using DraftEngine.Core.Models;

namespace DraftEngine.Documents.Builders;

/// <summary>
/// Converts a contract's PartiesJson into readable display text, party names and the
/// names used in the signature block.
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

        return string.IsNullOrWhiteSpace(partiesJson) ? DocumentDefaults.NotApplicable : partiesJson.Trim();
    }

    /// <summary>
    /// Resolves the two signatory labels used by both document formats. Parsed parties are
    /// authoritative; <c>Contract.ClientName</c> is only a fallback. This avoids printing the
    /// misleading literal "Counterparty" when data is missing and keeps PDF/DOCX consistent.
    /// </summary>
    internal static SignatureNames GetSignatureNames(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var names = GetNames(contract.PartiesJson);
        var clientName = string.IsNullOrWhiteSpace(contract.ClientName) ? null : contract.ClientName.Trim();

        if (names.Count >= 2)
        {
            return new SignatureNames(names[0], names[1]);
        }

        if (names.Count == 1)
        {
            var left = names[0];
            var right = clientName is not null && !string.Equals(clientName, left, StringComparison.OrdinalIgnoreCase)
                ? clientName
                : DocumentDefaults.FallbackSecondParty;
            return new SignatureNames(left, right);
        }

        return new SignatureNames(
            clientName ?? DocumentDefaults.FallbackFirstParty,
            DocumentDefaults.FallbackSecondParty);
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

/// <summary>Left/right signatory labels rendered in the signature block.</summary>
internal readonly record struct SignatureNames(string LeftName, string RightName);
