using System;

namespace DraftEngine.Core.Models;

/// <summary>
/// An immutable snapshot of a contract's assembled text, taken automatically before a
/// potentially destructive operation (an AI chat edit, a regeneration, or a revert) so the
/// user can always inspect earlier drafts and restore one. Versions are numbered per
/// contract starting at 1; the oldest snapshots are pruned beyond a fixed cap.
/// </summary>
public class ContractVersion
{
    /// <summary>Database identifier of this snapshot.</summary>
    public int Id { get; set; }

    /// <summary>Identifier of the contract this snapshot belongs to.</summary>
    public int ContractId { get; set; }

    /// <summary>One-based version number, unique per contract (1 is the oldest snapshot).</summary>
    public int VersionNumber { get; set; }

    /// <summary>The full assembled contract text as it was at snapshot time.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Why the snapshot was taken (e.g. "AI edit", "Regeneration", "Before revert"),
    /// shown in the version history list.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>UTC timestamp of when the snapshot was taken.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets the contract this snapshot belongs to.</summary>
    public Contract Contract { get; set; } = null!;
}
