using System.Collections.Generic;
using System.Threading.Tasks;
using DraftEngine.Core.Models;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Edits an existing contract (intake answers, metadata, clause selection) with a
/// version snapshot taken before any destructive change, plus duplicate and archive.
/// </summary>
public interface IContractEditService
{
    Task<Contract?> DuplicateContractAsync(int sourceContractId, string newTitle);

    /// <summary>Returns archived contracts belonging to the user, newest first.</summary>
    Task<List<Contract>> GetArchivedContractsAsync(int userId);

    /// <summary>Archives a contract; missing or already archived contracts are left unchanged.</summary>
    Task ArchiveContractAsync(int contractId);

    /// <summary>Restores an archived contract and returns true; returns false if it is missing or active.</summary>
    Task<bool> RestoreContractAsync(int contractId);

    /// <summary>Permanently deletes the contract and its owned data; missing contracts are ignored.</summary>
    Task PermanentDeleteContractAsync(int contractId);
    Task<Contract?> UpdateContractAsync(int contractId, Action<Contract> mutate);
}
