using System;
using DraftEngine.Core.Enums;

namespace DraftEngine.Core.Models;

/// <summary>
/// Represents a contract created by a user.
/// </summary>
public class Contract
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContractType { get; set; } = string.Empty;
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
    public string ClientName { get; set; } = string.Empty;
    public string ClientEmail { get; set; } = string.Empty;
    public string PartiesJson { get; set; } = string.Empty;
    public DateTime? EffectiveDate { get; set; }
    public string GoverningLaw { get; set; } = string.Empty;
    public int TermMonths { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastGeneratedAt { get; set; }
    public string FinalFilePath { get; set; } = string.Empty;
    public string AssembledText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user who owns the contract.
    /// </summary>
    public User User { get; set; } = null!;

    /// <summary>
    /// Gets the clause selections attached to the contract.
    /// </summary>
    public ICollection<ContractClause> ContractClauses { get; set; } = new List<ContractClause>();

    /// <summary>
    /// Gets the intake field answers attached to the contract.
    /// </summary>
    public ICollection<ContractField> ContractFields { get; set; } = new List<ContractField>();

    /// <summary>
    /// Gets the chat messages attached to the contract.
    /// </summary>
    public ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    /// <summary>
    /// Gets the documents generated from the contract.
    /// </summary>
    public ICollection<GeneratedDocument> GeneratedDocuments { get; set; } = new List<GeneratedDocument>();

    /// <summary>
    /// Gets the automatic text snapshots (version history) taken before AI edits,
    /// regenerations and reverts, so an earlier draft can always be restored.
    /// </summary>
    public ICollection<ContractVersion> Versions { get; set; } = new List<ContractVersion>();
}
