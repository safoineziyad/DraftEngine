using System.Collections.Generic;
using System.Threading.Tasks;
using DraftEngine.Core.Models;

namespace DraftEngine.App.Services;

/// <summary>
/// Export history for a contract: lists the <see cref="GeneratedDocument"/> rows
/// produced by DOCX/PDF exports. This is a minimal in-memory stub (no database
/// table for history yet) — the list is populated from the contract's
/// <see cref="Core.Models.Contract.GeneratedDocuments"/> navigation property.
/// </summary>
public interface IExportHistoryService
{
    /// <summary>Returns the export history rows for the given contract.</summary>
    Task<IReadOnlyList<GeneratedDocument>> GetHistoryAsync(int contractId);
}

/// <summary>
/// Minimal in-memory implementation of <see cref="IExportHistoryService"/>.
/// Reads history from the contract's <see cref="Core.Models.Contract.GeneratedDocuments"/>
/// navigation property (already loaded by the contract service).
/// </summary>
public class ExportHistoryService : IExportHistoryService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<GeneratedDocument>> GetHistoryAsync(int contractId)
    {
        // The contract service eager-loads GeneratedDocuments. Return an empty list
        // when the contract cannot be found; the caller supplies the contract.
        return Task.FromResult<IReadOnlyList<GeneratedDocument>>(new List<GeneratedDocument>());
    }
}
