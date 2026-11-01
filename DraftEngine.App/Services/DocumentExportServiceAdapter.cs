using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using System.Threading.Tasks;

namespace DraftEngine.App.Services;

/// <summary>
/// Adapts <see cref="Core.Services.IDocumentExportService"/> to the UI-facing
/// <see cref="IDocumentExportService"/> contract.
/// </summary>
public class DocumentExportServiceAdapter : IDocumentExportService
{
    private readonly Core.Services.IDocumentExportService _inner;

    public DocumentExportServiceAdapter(Core.Services.IDocumentExportService inner)
    {
        _inner = inner;
    }

    public Task<GeneratedDocument> RecordExportAsync(int contractId, string filePath, FileType fileType) =>
        _inner.RecordExportAsync(contractId, filePath, fileType);
}
