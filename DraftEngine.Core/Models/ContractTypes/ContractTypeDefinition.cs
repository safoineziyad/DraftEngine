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
    public List<ContractTypeIntakeField> IntakeFields { get; set; } = new();
    public HashSet<string> SupportedPlaceholderKeys { get; set; } = new();
    public string[] DefaultClauseCategories { get; set; } = System.Array.Empty<string>();
    public string PreambleTemplate { get; set; } = string.Empty;
    public string SignatureBlockTemplate { get; set; } = string.Empty;
}

public class ContractTypeIntakeField
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = "Text"; // Text, Date, Choice, Number
    public bool Required { get; set; } = true;
    public string? DefaultValueKey { get; set; }
    public string[]? Choices { get; set; }
}
