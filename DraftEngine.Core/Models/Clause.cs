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

    /// <summary>True if this clause was created by a user rather than seeded as system template.</summary>
    public bool IsSystem { get; set; } = true;

    /// <summary>
    /// True when the clause is user-created (as opposed to a seeded system clause). Kept in
    /// sync with <see cref="IsSystem"/> for older rows but stored explicitly so clause-selection
    /// queries can rely on a single, indexed flag.
    /// </summary>
    public bool IsUserDefined { get; set; }

    /// <summary>Owner user id for user-created clauses; null for system clauses.</summary>
    public int? OwnerUserId { get; set; }

    /// <summary>True if the clause has been archived by its owner (hidden from selection).</summary>
    public bool IsArchived { get; set; }

    /// <summary>Source/origin of the clause (e.g. "System", "User").</summary>
    public string Source { get; set; } = "System";

    /// <summary>
    /// Gets the contract clause selections that reference this clause template.
    /// </summary>
    public ICollection<ContractClause> ContractClauses { get; set; } = new List<ContractClause>();

    /// <summary>Gets the owner user if this is a user-owned clause.</summary>
    public User? OwnerUser { get; set; }
}
