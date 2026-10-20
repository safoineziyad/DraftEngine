namespace DraftEngine.Core.Models;

/// <summary>
/// Represents a clause selected for a specific contract.
/// </summary>
public class ContractClause
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public int ClauseId { get; set; }
    public bool Selected { get; set; } = true;
    public string CustomBodyOverride { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the contract the clause belongs to.
    /// </summary>
    public Contract Contract { get; set; } = null!;

    /// <summary>
    /// Gets or sets the clause template backing this selection.
    /// </summary>
    public Clause Clause { get; set; } = null!;
}
