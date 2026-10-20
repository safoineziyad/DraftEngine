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
}
