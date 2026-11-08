using System;
using System.Collections.Generic;
using DraftEngine.Core.Models.ContractTypes;

namespace DraftEngine.Core.Services;

/// <summary>
/// Builds the document title, review summary, preamble and signature templates from the
/// contract type registry. All methods are pure so the wizard and the generation pipeline can
/// be tested without WPF.
/// </summary>
public static class ContractContentBuilder
{
    /// <summary>A stable field-key/value pair for the wizard's localized review step.</summary>
    public sealed record ContractSummaryRow(string Label, string Value);

    /// <summary>
    /// Builds the contract title from the registry's party keys, e.g.
    /// "Mutual NDA – Acme Corp &amp; Globex Ltd" or "One-Way NDA – Acme Corp to Globex Ltd".
    /// Missing optional parties are simply omitted; with no parties the display name is used.
    /// </summary>
    public static string BuildTitle(
        ContractTypeDefinition definition,
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        var first = FirstNonEmpty(values, definition.FirstPartyNameKey, "Party1");
        var second = FirstNonEmpty(values, definition.SecondPartyNameKey, "Party2");

        if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(second))
        {
            return definition.DisplayName;
        }

        if (string.IsNullOrWhiteSpace(second))
        {
            return $"{definition.DisplayName} \u2013 {first}";
        }

        if (string.IsNullOrWhiteSpace(first))
        {
            return $"{definition.DisplayName} \u2013 {second}";
        }

        return definition.Id == ContractTypeRegistry.OneWayNdaId
            ? $"{definition.DisplayName} \u2013 {first} to {second}"
            : $"{definition.DisplayName} \u2013 {first} & {second}";
    }

    /// <summary>
    /// Builds the review-step rows from the registry's intake fields, in registry order.
    /// Optional fields the user left blank are skipped so the review screen stays readable.
    /// </summary>
    public static IReadOnlyList<ContractSummaryRow> BuildSummary(
        ContractTypeDefinition definition,
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        var rows = new List<ContractSummaryRow>();
        foreach (var field in definition.IntakeFields)
        {
            var value = FirstNonEmpty(values, field.Key);
            if (string.IsNullOrWhiteSpace(value) && !field.Required)
            {
                continue;
            }

            rows.Add(new ContractSummaryRow(field.Key, value));
        }

        return rows;
    }

    private static string FirstNonEmpty(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            foreach (var pair in values)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(pair.Value))
                {
                    return pair.Value.Trim();
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns the registry preamble template (the recital that names the type, the parties and
    /// the purpose). A neutral fallback is used only when a definition omits it, so a contract
    /// is never generated with an empty preamble.
    /// </summary>
    public static string BuildPreambleTemplate(ContractTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!string.IsNullOrWhiteSpace(definition.PreambleTemplate))
        {
            return definition.PreambleTemplate;
        }

        return $"This {definition.DisplayName} (the \"Agreement\") is entered into by and between " +
               $"{{{{{definition.FirstPartyNameKey}}}}} and {{{{{definition.SecondPartyNameKey}}}}}.";
    }

    /// <summary>
    /// Returns the registry signature block template, whose signatory lines carry the type's
    /// role labels (e.g. "Contractor", "Client"). A safe fallback is used only when a
    /// definition omits it.
    /// </summary>
    public static string BuildSignatureBlockTemplate(ContractTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!string.IsNullOrWhiteSpace(definition.SignatureBlockTemplate))
        {
            return definition.SignatureBlockTemplate;
        }

        return "IN WITNESS WHEREOF, the Parties have executed this Agreement as of the Effective Date.\n\n" +
               $"___________________________\n{{{{{definition.FirstPartyNameKey}}}}} ({definition.SignatureFirstPartyLabel})\n\n" +
               $"___________________________\n{{{{{definition.SecondPartyNameKey}}}}} ({definition.SignatureSecondPartyLabel})";
    }
}
