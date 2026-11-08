using System.Collections.Generic;

namespace DraftEngine.Core.Models.ContractTypes;

/// <summary>
/// Defines the canonical schema for a contract type.
/// </summary>
public class ContractTypeDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] PartyRoleLabels { get; set; } = System.Array.Empty<string>();
    public string SignatureFirstPartyLabel { get; set; } = string.Empty;
    public string SignatureSecondPartyLabel { get; set; } = string.Empty;

    /// <summary>Placeholder key holding the first party's name (e.g. <c>Party1</c> or <c>ContractorName</c>).</summary>
    public string FirstPartyNameKey { get; set; } = string.Empty;

    /// <summary>Placeholder key holding the first party's address.</summary>
    public string FirstPartyAddressKey { get; set; } = string.Empty;

    /// <summary>Placeholder key holding the second party's name (e.g. <c>Party2</c> or <c>ClientName</c>).</summary>
    public string SecondPartyNameKey { get; set; } = string.Empty;

    /// <summary>Placeholder key holding the second party's address.</summary>
    public string SecondPartyAddressKey { get; set; } = string.Empty;

    public List<ContractTypeIntakeField> IntakeFields { get; set; } = new();
    public HashSet<string> SupportedPlaceholderKeys { get; set; } = new();
    public int[] DefaultClauseIds { get; set; } = System.Array.Empty<int>();
    public string ClauseLookupName { get; set; } = string.Empty;
    public string[] DefaultClauseCategories { get; set; } = System.Array.Empty<string>();
    public string PreambleTemplate { get; set; } = string.Empty;
    public string SignatureBlockTemplate { get; set; } = string.Empty;

    /// <summary>Builds this contract type's preamble using the supplied placeholder values.</summary>
    public System.Func<System.Collections.Generic.IReadOnlyDictionary<string, string>, string> PreambleBuilder =>
        values => RenderTemplate(PreambleTemplate, values);

    private static string RenderTemplate(
        string template,
        System.Collections.Generic.IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            template = template.Replace(
                "{{" + pair.Key + "}}",
                pair.Value ?? string.Empty,
                System.StringComparison.OrdinalIgnoreCase);
        }

        return template;
    }
}

public class ContractTypeIntakeField
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = "Text"; // Text, Date, Choice, Number
    public bool Required { get; set; } = true;
    public string? DefaultValue { get; set; }
    public string? DefaultValueKey { get; set; }
    public string[]? Choices { get; set; }
}
