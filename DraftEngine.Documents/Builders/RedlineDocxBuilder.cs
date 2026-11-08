using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DraftEngine.Core.Services;

namespace DraftEngine.Documents.Builders;

/// <summary>
/// Produces a DOCX with real tracked changes: deleted lines are wrapped in w:del and added
/// lines in w:ins, both authored "DraftEngine". The file opens in Word with change tracking.
/// </summary>
public sealed class RedlineDocxBuilder
{
    private int _revisionId = 1;

    public Task<string> BuildRedlineAsync(
        string olderText,
        string newerText,
        string outputPath,
        string author = "DraftEngine",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(olderText);
        ArgumentNullException.ThrowIfNull(newerText);
        ArgumentNullException.ThrowIfNull(outputPath);

        return Task.Run(() =>
        {
            var diffs = ContractTextDiff.Compute(olderText, newerText, int.MaxValue);

            using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());

            var body = main.Document.Body!;
            var timestamp = DateTime.UtcNow;

            foreach (var line in diffs)
            {
                var paragraph = new Paragraph();
                switch (line.Kind)
                {
                    case DiffLineKind.Removed:
                        paragraph.Append(new DeletedRun(
                            new RunProperties(new Color { Val = "B91C1C" }, new Strike()))
                        {
                            Author = author,
                            Date = timestamp,
                            Id = (_revisionId++).ToString()
                        });
                        paragraph.LastChild!.Append(new DeletedText(line.Text) { Space = SpaceProcessingModeValues.Preserve });
                        break;

                    case DiffLineKind.Added:
                        paragraph.Append(new InsertedRun(
                            new RunProperties(new Color { Val = "166534" }, new Underline { Val = UnderlineValues.Single }))
                        {
                            Author = author,
                            Date = timestamp,
                            Id = (_revisionId++).ToString()
                        });
                        paragraph.LastChild!.Append(new Text(line.Text) { Space = SpaceProcessingModeValues.Preserve });
                        break;

                    default:
                        paragraph.Append(new Run(new Text(line.Text) { Space = SpaceProcessingModeValues.Preserve }));
                        break;
                }

                body.Append(paragraph);
            }

            main.Document.Save();
            return outputPath;
        }, cancellationToken);
    }
}
