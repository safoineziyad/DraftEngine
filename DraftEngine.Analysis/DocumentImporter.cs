using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace DraftEngine.Analysis;

/// <summary>Imports DOCX/PDF/TXT into a single normalized document representation.</summary>
public static class DocumentImporter
{
    private static readonly Regex NumberedHeading = new(
        @"^\s*(?:\d+(?:\.\d+)*[.)]?|[IVXLC]+[.)]|ARTICLE\s+\d+|SECTION\s+\d+)\s+\S",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AllCapsHeading = new(
        @"^[A-Z][A-Z0-9 ,'&/\-]{3,80}$",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> SupportedExtensions { get; } = [".docx", ".pdf", ".txt"];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Imports the file, choosing the reader by extension.</summary>
    public static Task<NormalizedDocument> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new DocumentImportException($"The file '{Path.GetFileName(path)}' does not exist.");
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".docx" => Task.Run(() => ImportDocx(path), cancellationToken),
            ".pdf" => Task.Run(() => ImportPdf(path), cancellationToken),
            ".txt" => Task.Run(() => ImportText(path), cancellationToken),
            _ => throw new DocumentImportException("Only DOCX, PDF and TXT files are supported.")
        };
    }

    private static NormalizedDocument ImportDocx(string path)
    {
        try
        {
            using var document = WordprocessingDocument.Open(path, isEditable: false);
            var body = document.MainDocumentPart?.Document?.Body
                ?? throw new DocumentImportException("The DOCX file has no document body.");

            var paragraphs = new List<DocumentParagraph>();
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                var text = string.Concat(paragraph.Descendants<Text>().Select(run => run.Text)).Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                paragraphs.Add(new DocumentParagraph(
                    $"p{paragraphs.Count + 1}",
                    paragraphs.Count,
                    text,
                    0,
                    0,
                    IsHeadingParagraph(paragraph, text)));
            }

            if (paragraphs.Count == 0)
            {
                throw new DocumentImportException("The DOCX file contains no readable text.");
            }

            return Build(path, DocumentFormat.Docx, paragraphs, hasTextLayer: true);
        }
        catch (DocumentImportException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DocumentImportException("The DOCX file could not be read. It may be corrupt or password-protected.", ex);
        }
    }

    private static bool IsHeadingParagraph(Paragraph paragraph, string text)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (!string.IsNullOrEmpty(styleId) &&
            (styleId.Contains("Heading", StringComparison.OrdinalIgnoreCase) ||
             styleId.Equals("Title", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var outline = paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
        if (outline is >= 0 and <= 8)
        {
            return true;
        }

        return NumberedHeading.IsMatch(text) || AllCapsHeading.IsMatch(text);
    }

    private static NormalizedDocument ImportPdf(string path)
    {
        try
        {
            using var document = PdfDocument.Open(path);
            var lines = new List<string>();
            foreach (var page in document.GetPages())
            {
                var pageText = page.Text;
                foreach (var raw in pageText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length > 0)
                    {
                        lines.Add(line);
                    }
                }
            }

            var hasText = lines.Sum(line => line.Length) >= 20;
            if (!hasText)
            {
                // A scanned PDF: report the limitation instead of pretending there is nothing to review.
                return Build(path, DocumentFormat.Pdf, [], hasTextLayer: false);
            }

            var paragraphs = lines.Select((line, index) => new DocumentParagraph(
                $"p{index + 1}",
                index,
                line,
                0,
                0,
                NumberedHeading.IsMatch(line) || AllCapsHeading.IsMatch(line))).ToList();

            return Build(path, DocumentFormat.Pdf, paragraphs, hasTextLayer: true);
        }
        catch (DocumentImportException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DocumentImportException("The PDF file could not be read. It may be corrupt or password-protected.", ex);
        }
    }

    private static NormalizedDocument ImportText(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select((line, index) => new DocumentParagraph(
                $"p{index + 1}",
                index,
                line,
                0,
                0,
                NumberedHeading.IsMatch(line) || AllCapsHeading.IsMatch(line)))
            .ToList();

        if (paragraphs.Count == 0)
        {
            throw new DocumentImportException("The text file contains no readable text.");
        }

        return Build(path, DocumentFormat.PlainText, paragraphs, hasTextLayer: true);
    }

    private static NormalizedDocument Build(
        string path,
        DocumentFormat format,
        List<DocumentParagraph> paragraphs,
        bool hasTextLayer)
    {
        var builder = new StringBuilder();
        var normalized = new List<DocumentParagraph>(paragraphs.Count);
        foreach (var paragraph in paragraphs)
        {
            var start = builder.Length;
            builder.Append(paragraph.Text);
            var end = builder.Length;
            normalized.Add(paragraph with { StartOffset = start, EndOffset = end });
            builder.Append('\n');
        }

        return new NormalizedDocument
        {
            FileName = Path.GetFileName(path),
            Format = format,
            Text = builder.ToString(),
            Paragraphs = normalized,
            HasTextLayer = hasTextLayer
        };
    }
}
