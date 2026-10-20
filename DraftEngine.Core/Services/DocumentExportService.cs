using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Core.Services;

/// <summary>
/// Records an exported document (DOCX/PDF) for a contract: creates the
/// <see cref="GeneratedDocument"/> row with the next version number and points the
/// contract's FinalFilePath at the newest export.
/// </summary>
public interface IDocumentExportService
{
    /// <summary>
    /// Records an exported file and updates the contract's FinalFilePath.
    /// Throws a friendly InvalidOperationException when the contract does not exist.
    /// </summary>
    Task<GeneratedDocument> RecordExportAsync(int contractId, string filePath, FileType fileType);
}

/// <summary>EF Core implementation of <see cref="IDocumentExportService"/>.</summary>
public class DocumentExportService : IDocumentExportService
{
    private readonly DbContext _database;

    /// <summary>Creates the export recording service.</summary>
    /// <param name="database">EF Core context hosting the DraftEngine model.</param>
    public DocumentExportService(DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public async Task<GeneratedDocument> RecordExportAsync(int contractId, string filePath, FileType fileType)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException("The export did not produce a file path.");
        }

        var contract = await _database.Set<Contract>().FirstOrDefaultAsync(c => c.Id == contractId)
            ?? throw new InvalidOperationException(
                "This contract could not be found. Open it again from the Contracts list.");

        var highestVersion = await _database.Set<GeneratedDocument>()
            .Where(document => document.ContractId == contractId)
            .Select(document => (int?)document.Version)
            .MaxAsync();

        var document = new GeneratedDocument
        {
            ContractId = contractId,
            FilePath = filePath,
            FileType = fileType,
            GeneratedAt = DateTime.UtcNow,
            Version = (highestVersion ?? 0) + 1,
        };

        contract.FinalFilePath = filePath;
        contract.Status = ContractStatus.Exported;
        _database.Set<GeneratedDocument>().Add(document);
        await _database.SaveChangesAsync();
        return document;
    }
}
