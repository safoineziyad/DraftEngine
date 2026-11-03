using System.Threading.Tasks;
using DraftEngine.Core.Models;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Edits an existing contract (intake answers, metadata, clause selection) with a
/// version snapshot taken before any destructive change, plus duplicate and archive.
/// </summary>
public interface IContractEditService
{
    Task<Contract> DuplicateContractAsync(int sourceContractId, string newTitle);
    Task ArchiveContractAsync(int contractId);
    Task<bool> RestoreContractAsync(int contractId);
    Task PermanentDeleteContractAsync(int contractId);
    Task<Contract> UpdateContractAsync(int contractId, Action<Contract> mutate);
}
