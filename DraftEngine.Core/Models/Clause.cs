namespace DraftEngine.Core.Models;

/// <summary>
/// Represents a clause template for a contract type.
/// </summary>
public class Clause
{
    public int Id { get; set; }
    public string ContractType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string BodyTemplate { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
    public bool IsDefaultSelected { get; set; }
    public int SortOrder { get; set; }

    /// <summary>
    /// Gets the contract clause selections that reference this clause template.
    /// </summary>
    public ICollection<ContractClause> ContractClauses { get; set; } = new List<ContractClause>();
}
