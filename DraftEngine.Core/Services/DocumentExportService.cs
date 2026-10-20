using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Core.Services;

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

/// <summary>EF Core implementation of <see cref="IDocumentExportService"/>.</summary>
public class DocumentExportService : IDocumentExportService
{
    private readonly IDatabaseContextFactory _databaseFactory;

    /// <summary>Creates the export recording service.</summary>
    /// <param name="databaseFactory">Factory handing out one short-lived context per operation.</param>
    public DocumentExportService(IDatabaseContextFactory databaseFactory)
    {
        ArgumentNullException.ThrowIfNull(databaseFactory);
        _databaseFactory = databaseFactory;
    }

    /// <inheritdoc />
    public async Task<GeneratedDocument> RecordExportAsync(int contractId, string filePath, FileType fileType)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException("The export did not produce a file path.");
        }

        using var database = _databaseFactory.CreateContext();

        var contract = await database.Set<Contract>().FirstOrDefaultAsync(c => c.Id == contractId)
            ?? throw new InvalidOperationException(
                "This contract could not be found. Open it again from the Contracts list.");

        var highestVersion = await database.Set<GeneratedDocument>()
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
        database.Set<GeneratedDocument>().Add(document);
        await database.SaveChangesAsync();
        return document;
    }
}
