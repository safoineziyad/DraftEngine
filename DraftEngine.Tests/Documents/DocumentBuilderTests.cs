using DocumentFormat.OpenXml.Packaging;
using DraftEngine.Core.Models;
using DraftEngine.Documents.Builders;

namespace DraftEngine.Tests.Documents;

/// <summary>
/// Integration tests for the document builders (DOCX via Open XML, PDF via QuestPDF).
/// Files are written to a temp folder and cleaned up afterwards; no network calls.
/// </summary>
public class DocumentBuilderTests : IDisposable
{
    private const string SampleContractText = """
        1. Confidentiality
        Each party (the "Receiving Party") shall keep confidential all Technical Information disclosed by the other party (the "Disclosing Party") and shall not disclose it to any third party without the prior written consent of the Disclosing Party. The Receiving Party shall use the same degree of care it uses to protect its own confidential information, but no less than reasonable care.

        2. Term and Termination
        This Agreement commences on the Effective Date and continues for twelve (12) months unless terminated earlier by either party upon thirty (30) days prior written notice. Obligations under Section 1 (Confidentiality) survive termination for a period of three (3) years.

        3. Return of Materials
        Upon termination or upon the Disclosing Party's written request, the Receiving Party shall promptly return or destroy all documents, copies and media containing Technical Information, and shall certify such return or destruction in writing.

        4. Governing Law
        This Agreement shall be governed by and construed in accordance with the laws of England and Wales, and the parties submit to the exclusive jurisdiction of its courts.
        """;

    private readonly string _outputDir;

    public DocumentBuilderTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "draftengine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_outputDir))
            {
                Directory.Delete(_outputDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp folder.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of the temp folder.
        }
    }

    private static Contract CreateSampleContract() => new()
    {
        Title = "Mutual Non-Disclosure Agreement",
        ContractType = "NDA",
        ClientName = "Acme Corp",
        ClientEmail = "legal@acme.example",
        PartiesJson = """[{"name":"Acme Corp","role":"Disclosing Party"},{"name":"Globex Ltd","role":"Receiving Party"}]""",
        EffectiveDate = new DateTime(2026, 1, 15),
        GoverningLaw = "England and Wales",
        TermMonths = 12
    };

    [Fact]
    public async Task BuildDocxAsync_WithSampleContract_ProducesValidDocx()
    {
        var builder = new DocxBuilder();
        var path = Path.Combine(_outputDir, "nda.docx");

        var result = await builder.BuildDocxAsync(CreateSampleContract(), SampleContractText, path);

        Assert.Equal(path, result);
        Assert.True(File.Exists(path), "DOCX file was not created.");
        Assert.True(new FileInfo(path).Length > 1024, "DOCX file is trivially small.");

        using var document = WordprocessingDocument.Open(path, false);
        Assert.NotNull(document.MainDocumentPart);
        Assert.NotNull(document.MainDocumentPart!.Document);
        Assert.NotNull(document.MainDocumentPart.Document.Body);
        Assert.Contains("Confidentiality", document.MainDocumentPart.Document.Body!.InnerText);
    }

    [Fact]
    public async Task BuildPdfAsync_WithSampleContract_ProducesValidPdf()
    {
        var builder = new PdfBuilder();
        var path = Path.Combine(_outputDir, "nda.pdf");

        var result = await builder.BuildPdfAsync(CreateSampleContract(), SampleContractText, path);

        Assert.Equal(path, result);
        Assert.True(File.Exists(path), "PDF file was not created.");
        Assert.True(new FileInfo(path).Length > 1024, "PDF file is trivially small.");

        var header = new byte[4];
        using (var stream = File.OpenRead(path))
        {
            await stream.ReadExactlyAsync(header, 0, 4);
        }

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(header));
    }

    [Fact]
    public async Task DocumentBuilder_DelegatesToSpecializedBuilders()
    {
        var builder = new DocumentBuilder(new DocxBuilder(), new PdfBuilder());
        var docxPath = Path.Combine(_outputDir, "composite.docx");
        var pdfPath = Path.Combine(_outputDir, "composite.pdf");

        await builder.BuildDocxAsync(CreateSampleContract(), SampleContractText, docxPath);
        await builder.BuildPdfAsync(CreateSampleContract(), SampleContractText, pdfPath);

        Assert.True(File.Exists(docxPath));
        Assert.True(File.Exists(pdfPath));
    }

    [Fact]
    public async Task BuildDocxAsync_CreatesMissingOutputDirectory()
    {
        var builder = new DocxBuilder();
        var path = Path.Combine(_outputDir, "nested", "deeper", "nda.docx");

        await builder.BuildDocxAsync(CreateSampleContract(), SampleContractText, path);

        Assert.True(File.Exists(path));
    }
}
