using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.App.Services;

/// <summary>Indicates whether an exported document can be found at its recorded path.</summary>
public enum ExportFileStatus
{
    Available,
    Missing,
    InvalidPath
}

/// <summary>Queries persisted export history for a contract.</summary>
public interface IExportHistoryService
{
    /// <summary>Returns the export history rows for the given contract, newest first.</summary>
    Task<IReadOnlyList<GeneratedDocument>> GetHistoryAsync(int contractId);

    /// <summary>Checks whether the export recorded by a history row is available.</summary>
    ExportFileStatus GetFileStatus(GeneratedDocument document);
}

/// <summary>
/// Reads persisted DOCX/PDF export metadata. Export contents are not loaded or logged.
/// </summary>
public class ExportHistoryService : IExportHistoryService
{
    private readonly IDatabaseContextFactory _databaseFactory;

    /// <summary>Creates a history service using the application's database context factory.</summary>
    public ExportHistoryService(IDatabaseContextFactory databaseFactory)
    {
        ArgumentNullException.ThrowIfNull(databaseFactory);
        _databaseFactory = databaseFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GeneratedDocument>> GetHistoryAsync(int contractId)
    {
        using var database = _databaseFactory.CreateContext();
        return await database.Set<GeneratedDocument>()
            .AsNoTracking()
            .Where(document => document.ContractId == contractId)
            .OrderByDescending(document => document.GeneratedAt)
            .ThenByDescending(document => document.Id)
            .ToListAsync();
    }

    /// <inheritdoc />
    public ExportFileStatus GetFileStatus(GeneratedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return GetFileStatus(document.FilePath);
    }

    /// <summary>Checks a path without reading or logging the exported document contents.</summary>
    public static ExportFileStatus GetFileStatus(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ExportFileStatus.InvalidPath;
        }

        try
        {
            return File.GetAttributes(filePath).HasFlag(FileAttributes.Directory)
                ? ExportFileStatus.InvalidPath
                : ExportFileStatus.Available;
        }
        catch (FileNotFoundException)
        {
            return ExportFileStatus.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return ExportFileStatus.Missing;
        }
    }
}
