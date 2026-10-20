using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;

namespace DraftEngine.Documents.Builders;

/// <summary>
/// Composite document builder that delegates DOCX generation to <see cref="DocxBuilder"/>
/// and PDF generation to <see cref="PdfBuilder"/>. Register this as IDocumentBuilder in DI.
/// </summary>
public class DocumentBuilder : IDocumentBuilder
{
    private readonly IDocxBuilder _docxBuilder;
    private readonly IPdfBuilder _pdfBuilder;

    public DocumentBuilder(IDocxBuilder docxBuilder, IPdfBuilder pdfBuilder)
    {
        ArgumentNullException.ThrowIfNull(docxBuilder);
        ArgumentNullException.ThrowIfNull(pdfBuilder);
        _docxBuilder = docxBuilder;
        _pdfBuilder = pdfBuilder;
    }

    public Task<string> BuildDocxAsync(Contract contract, string fullContractText, string outputPath)
    {
        return _docxBuilder.BuildDocxAsync(contract, fullContractText, outputPath);
    }

    public Task<string> BuildPdfAsync(Contract contract, string fullContractText, string outputPath)
    {
        return _pdfBuilder.BuildPdfAsync(contract, fullContractText, outputPath);
    }
}
