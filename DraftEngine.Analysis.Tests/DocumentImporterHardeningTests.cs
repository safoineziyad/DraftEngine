using System.IO.Compression;
using DraftEngine.Analysis;

namespace DraftEngine.Analysis.Tests;

/// <summary>Hostile import inputs are rejected before parsing, without huge allocations.</summary>
public sealed class DocumentImporterHardeningTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "de-import-" + Guid.NewGuid().ToString("N"));

    public DocumentImporterHardeningTests() => Directory.CreateDirectory(_directory);

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

    [Fact]
    public async Task TextFileAboveTheSizeLimit_IsRejected()
    {
        var path = Path.Combine(_directory, "huge.txt");
        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            stream.SetLength(DocumentImporter.MaxInputBytes + 1);
        }

        var error = await Assert.ThrowsAsync<DocumentImportException>(
            () => DocumentImporter.ImportAsync(path));

        Assert.Contains("limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DocxWithAZipBombEntry_IsRejectedBeforeOfficeOpenXmlParsesIt()
    {
        var path = Path.Combine(_directory, "bomb.docx");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            // 8 MB of zeros compresses to a few KB: ratio far above the allowed maximum.
            var entry = archive.CreateEntry("word/document.xml", CompressionLevel.SmallestSize);
            using var stream = entry.Open();
            stream.Write(new byte[8 * 1024 * 1024]);
        }

        var error = await Assert.ThrowsAsync<DocumentImportException>(
            () => DocumentImporter.ImportAsync(path));

        Assert.Contains("compression ratio", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DocxWithTooManyEntries_IsRejected()
    {
        var path = Path.Combine(_directory, "many.docx");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            for (var index = 0; index < DocumentImporter.MaxZipEntries + 1; index++)
            {
                archive.CreateEntry($"part-{index}.xml");
            }
        }

        var error = await Assert.ThrowsAsync<DocumentImportException>(
            () => DocumentImporter.ImportAsync(path));

        Assert.Contains("too many entries", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
