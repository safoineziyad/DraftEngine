using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Records an exported document (DOCX/PDF) for a contract: creates the
/// <see cref="GeneratedDocument"/> row with the next version number and points the
/// contract's FinalFilePath at the newest export. Stateless: each operation opens
/// its own short-lived context via <see cref="IDatabaseContextFactory"/>.
/// </summary>
public interface IDocumentExportService
{
    /// <summary>
    /// Records an exported file and updates the contract's FinalFilePath.
    /// Throws a friendly InvalidOperationException when the contract does not exist.
    /// </summary>
    Task<GeneratedDocument> RecordExportAsync(int contractId, string filePath, FileType fileType);
}
