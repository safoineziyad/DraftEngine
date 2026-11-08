using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DraftEngine.Analysis;
using DraftEngine.Documents.Builders;

namespace DraftEngine.Analysis.Tests;

public sealed class ExportTests : IDisposable
{
    private readonly string _directory = Fixtures.TempDirectory();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<AnalysisResult> ResultAsync()
    {
        var path = Fixtures.CreateDocx(_directory, Fixtures.GoodMutualNdaParagraphs);
        var document = await DocumentImporter.ImportAsync(path);
        return await new ContractAnalyzer().AnalyzeAsync(document,
            new AnalysisOptions { ContractType = PlaybookCatalog.MutualNda });
    }

    [Fact]
    public async Task Redline_ContainsTrackedInsertionsAndDeletions()
    {
        var output = Path.Combine(_directory, "redline.docx");
        await new RedlineDocxBuilder().BuildRedlineAsync(
            "1. Term\nThis agreement lasts two years.",
            "1. Term\nThis agreement lasts five years.\n2. Notices\nNotices must be written.",
            output);

        using var archive = ZipFile.OpenRead(output);
        var entry = archive.GetEntry("word/document.xml");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        var xml = await reader.ReadToEndAsync();

        Assert.Contains("<w:ins ", xml, StringComparison.Ordinal);
        Assert.Contains("<w:del ", xml, StringComparison.Ordinal);
        Assert.Contains("w:author=\"DraftEngine\"", xml, StringComparison.Ordinal);
        Assert.Contains("Notices must be written.", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportDocx_ContainsDisclaimerAndFindings()
    {
        var result = await ResultAsync();
        var output = Path.Combine(_directory, "report.docx");
        await new AnalysisReportBuilder().BuildDocxAsync(result, output);

        using var document = WordprocessingDocument.Open(output, false);
        var text = document.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Contains(AnalysisResult.Disclaimer, text, StringComparison.Ordinal);
        Assert.Contains("Contract review report", text, StringComparison.Ordinal);
        Assert.Contains("Confidentiality", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportPdf_IsGeneratedAndNonEmpty()
    {
        var result = await ResultAsync();
        var output = Path.Combine(_directory, "report.pdf");
        await new AnalysisReportBuilder().BuildPdfAsync(result, output);

        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 1000);
        var header = new byte[4];
        await using var stream = File.OpenRead(output);
        _ = await stream.ReadAsync(header);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(header));
    }
}
