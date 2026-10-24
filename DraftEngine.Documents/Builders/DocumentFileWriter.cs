namespace DraftEngine.Documents.Builders;

/// <summary>
/// Writes generated documents through a temporary file in the destination directory and only
/// publishes the result with an atomic move. A failed export therefore never truncates or
/// corrupts an existing document, and no partial <c>.tmp</c> files are left behind.
/// </summary>
internal static class DocumentFileWriter
{
    private static readonly byte[] PdfHeader = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipHeader = "PK"u8.ToArray();

    /// <summary>
    /// Runs <paramref name="writeFile"/> against a temporary path and moves the completed file
    /// to <paramref name="outputPath"/>. The destination directory is created when missing.
    /// </summary>
    /// <param name="outputPath">Final destination path.</param>
    /// <param name="writeFile">Callback that writes a complete document to the supplied path.</param>
    internal static void WriteAtomically(string outputPath, Action<string> writeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(writeFile);

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Output path must include a valid directory.", nameof(outputPath));
        }

        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            writeFile(tempPath);

            var generated = new FileInfo(tempPath);
            if (!generated.Exists || generated.Length == 0)
            {
                throw new IOException("The document generator produced an empty file.");
            }

            // Same-volume move with overwrite is an atomic replace on Windows: readers either
            // see the previous complete document or the new complete document, never a partial.
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>Verifies that a generated PDF begins with the required <c>%PDF-</c> header.</summary>
    internal static void ValidatePdf(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[PdfHeader.Length];
        if (stream.Read(header) != header.Length || !header.SequenceEqual(PdfHeader))
        {
            throw new InvalidDataException("The generated file is not a valid PDF document.");
        }
    }

    /// <summary>Verifies that a generated DOCX is a non-empty Open XML (ZIP) package.</summary>
    internal static void ValidateDocx(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[ZipHeader.Length];
        if (stream.Read(header) != header.Length || !header.SequenceEqual(ZipHeader))
        {
            throw new InvalidDataException("The generated file is not a valid DOCX package.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; the original export exception is more useful to the caller.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup; the original export exception is more useful to the caller.
        }
    }
}
