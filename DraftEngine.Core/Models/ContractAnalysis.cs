using System;

namespace DraftEngine.Core.Models;

/// <summary>
/// A saved, encrypted review result. <see cref="ResultJson"/> contains the normalized
/// document, clauses, findings and facts; it is encrypted at rest like contract text.
/// </summary>
public class ContractAnalysis
{
    public int Id { get; set; }

    /// <summary>Optional link to a contract; a standalone imported document leaves this null.</summary>
    public int? ContractId { get; set; }

    /// <summary>Original file name, encrypted because it can contain a client name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Canonical contract type used to select the playbook.</summary>
    public string ContractType { get; set; } = string.Empty;

    /// <summary>Serialized <c>AnalysisResult</c>, encrypted at rest.</summary>
    public string ResultJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public Contract? Contract { get; set; }
}
