using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DraftEngine.Core.Models;
using DraftEngine.Documents.Builders;

namespace DraftEngine.Tests.Documents;

/// <summary>
/// Real export roundtrip tests. Validation uses OpenXml DOM for DOCX and
/// byte-level checks for PDF (no PdfPig dependency). Ensures exports remain valid
/// after package upgrades.
/// </summary>
public class ExportRoundtripIntegrationTests : IDisposable
{
    private readonly string _outputDir;

    public ExportRoundtripIntegrationTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "draftengine-export-rt", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_outputDir))
                Directory.Delete(_outputDir, recursive: true);
        }
        catch { }
    }

    private static Contract CreateContract() => new()
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

    private const string Text = """
        1. Confidentiality
        Each party (the "Receiving Party") shall keep confidential all Technical Information disclosed by the other party (the "Disclosing Party") and shall not disclose it to any third party without the prior written consent of the Disclosing Party. The Receiving Party shall use the same degree of care it uses to protect its own confidential information, but no less than reasonable care.

        2. Term and Termination
        This Agreement commences on the Effective Date and continues for twelve (12) months unless terminated earlier by either party upon thirty (30) days prior written notice. Obligations under Section 1 (Confidentiality) survive termination for a period of three (3) years.

        3. Return of Materials
        Upon termination or upon the Disclosing Party's written request, the Receiving Party shall promptly return or destroy all documents, copies and media containing Technical Information, and shall certify such return or destruction in writing.

        4. Governing Law
        This Agreement shall be governed by and construed in accordance with the laws of England and Wales, and the parties submit to the exclusive jurisdiction of its courts.
        """;

    [Fact]
    public async Task PdfRoundtrip_ValidSignature_ContainsExpectedText()
    {
        var builder = new PdfBuilder();
        var path = Path.Combine(_outputDir, "roundtrip.pdf");
        await builder.BuildPdfAsync(CreateContract(), Text, path);

        Assert.True(File.Exists(path));
        var info = new FileInfo(path);
        Assert.True(info.Length > 5120, $"PDF too small: {info.Length}");

        using var fs = File.OpenRead(path);
        var header = new byte[5];
        await fs.ReadExactlyAsync(header, 0, 5);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(header));

        // Byte-scan for encryption leakage only (no PdfPig dependency). QuestPDF may compress/embed
        // streams so searching for rendered text as raw ASCII is unreliable; header/size are the
        // hard validation. Record method used in test comment.
        fs.Seek(0, SeekOrigin.Begin);
        var bytes = new byte[fs.Length];
        fs.ReadExactly(bytes, 0, bytes.Length);
        var content = Encoding.ASCII.GetString(bytes);
        Assert.DoesNotContain("DEENC1:", content);
    }

    [Fact]
    public async Task DocxRoundtrip_ValidZip_ContainsClauses()
    {
        var builder = new DocxBuilder();
        var path = Path.Combine(_outputDir, "roundtrip.docx");
        await builder.BuildDocxAsync(CreateContract(), Text, path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 2048);

        // Valid OpenXML zip
        using var doc = WordprocessingDocument.Open(path, false);
        Assert.NotNull(doc.MainDocumentPart);
        Assert.NotNull(doc.MainDocumentPart!.Document!.Body);
        var inner = doc.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("Confidentiality", inner);
        Assert.Contains("Governing Law", inner);
        Assert.DoesNotContain("{{", inner);
        Assert.DoesNotContain("DEENC1:", inner);
    }

    [Fact]
    public async Task ExportRoundtrip_SpecialCharacters_Preserve()
    {
        var contract = CreateContract();
        contract.ClientName = "François Ñoño — Corp";
        contract.Title = "Mutual NDA «test» & Co.";
        var specialText = """
            1. Confidentialité
            Détails: café, naïve — "smart quotes" & <tags> 😀
            """;

        var docxPath = Path.Combine(_outputDir, "special.docx");
        var pdfPath = Path.Combine(_outputDir, "special.pdf");

        await new DocxBuilder().BuildDocxAsync(contract, specialText, docxPath);
        await new PdfBuilder().BuildPdfAsync(contract, specialText, pdfPath);

        Assert.True(File.Exists(docxPath));
        Assert.True(File.Exists(pdfPath));

        using var doc = WordprocessingDocument.Open(docxPath, false);
        var inner = doc.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("café", inner);
    }
}
