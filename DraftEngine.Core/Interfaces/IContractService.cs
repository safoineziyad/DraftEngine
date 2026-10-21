using System.Collections.Generic;
using System.Threading.Tasks;
using DraftEngine.Core.Models;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Service for managing contracts.
/// </summary>
public interface IContractService
{
    /// <summary>Creates a new contract and returns it with its generated Id.</summary>
    Task<Contract> CreateAsync(Contract contract);

    /// <summary>Returns the contract with the given Id, or null when it does not exist.</summary>
    Task<Contract?> GetByIdAsync(int id);

    /// <summary>Returns every contract owned by the given user, newest first.</summary>
    Task<List<Contract>> GetAllAsync(int userId);

    /// <summary>Updates an existing contract; returns null when it no longer exists.</summary>
    Task<Contract?> UpdateAsync(Contract contract);

    /// <summary>Deletes a contract and its dependent rows. No-op when the contract does not exist.</summary>
    Task DeleteAsync(int id);

    /// <summary>
    /// Regenerates the contract document. When <paramref name="selectedClauseIds"/> is non-empty
    /// the stored clause selection is synced to it first; an empty list keeps the stored selection.
    /// AI polishing runs only when <paramref name="useAiPolish"/> is true (and an AI service is
    /// configured). Returns the assembled document text.
    /// </summary>
    Task<string> GenerateContractAsync(int contractId, List<int> selectedClauseIds, bool useAiPolish = false);

    /// <summary>
    /// Applies a natural-language edit to the stored contract text via AI, records the chat
    /// messages and returns the updated text. Throws an InvalidOperationException carrying a
    /// user-friendly message when the edit fails.
    /// </summary>
    Task<string> ApplyChatEditAsync(int contractId, string userInstruction);

    /// <summary>
    /// Returns the version history of a contract (automatic snapshots taken before AI edits,
    /// regenerations and reverts), oldest first. Missing contracts yield an empty list.
    /// </summary>
    Task<IReadOnlyList<ContractVersion>> GetVersionsAsync(int contractId);

    /// <summary>
    /// Restores the contract's assembled text from an earlier snapshot. The text being
    /// replaced is snapshotted first, so a revert can itself be undone. Returns the
    /// restored text. Throws an InvalidOperationException with a user-friendly message
    /// when the contract or the snapshot no longer exists.
    /// </summary>
    Task<string> RevertToVersionAsync(int contractId, int versionId);
}
