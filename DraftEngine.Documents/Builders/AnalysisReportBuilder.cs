using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DraftEngine.Analysis;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;
using WordColor = DocumentFormat.OpenXml.Wordprocessing.Color;

namespace DraftEngine.Documents.Builders;

/// <summary>Exports a review report to DOCX or PDF, with the mandatory disclaimer first.</summary>
public sealed class AnalysisReportBuilder
{
    public Task<string> BuildDocxAsync(AnalysisResult result, string outputPath, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var main = document.AddMainDocumentPart();
            main.Document = new WordDocument(new Body());
            var body = main.Document.Body!;

            body.Append(Heading("Contract review report", 32, bold: true));
            body.Append(ParagraphOf(result.Document.FileName, italic: true));
            body.Append(ParagraphOf($"Playbook: {result.Playbook.DisplayName} ({result.Playbook.ContractType})"));
            body.Append(ParagraphOf($"Generated: {DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)}"));
            body.Append(Disclaimer());

            body.Append(Heading("Summary", 24, bold: true));
            body.Append(ParagraphOf($"{result.Findings.Count} findings across {result.Clauses.Count} clauses. " +
                                    (result.Statistics.AiUsed ? "AI review was used; ungrounded AI findings were discarded." : "Deterministic review only; no AI was used.")));

            if (result.Facts.Count > 0)
            {
                body.Append(Heading("Extracted terms", 22, bold: true));
                foreach (var fact in result.Facts)
                {
                    body.Append(ParagraphOf($"{fact.Name}: {(fact.Found ? fact.Value : "not found")}"));
                }
            }

            body.Append(Heading("Findings", 24, bold: true));
            foreach (var finding in result.Findings)
            {
                body.Append(Heading($"[{finding.Severity}] {finding.Title}", 21, bold: true));
                body.Append(ParagraphOf($"Category: {finding.Category}"));
                if (!string.IsNullOrWhiteSpace(finding.Quote))
                {
                    body.Append(ParagraphOf($"Quote: \"{finding.Quote}\"", italic: true));
                }

                body.Append(ParagraphOf($"Why it matters: {finding.Explanation}"));
                if (!string.IsNullOrWhiteSpace(finding.SuggestedFix))
                {
                    body.Append(ParagraphOf($"Suggested fix: {finding.SuggestedFix}"));
                }
            }

            body.Append(Disclaimer());
            main.Document.Save();
            return outputPath;
        }, cancellationToken);

    public Task<string> BuildPdfAsync(AnalysisResult result, string outputPath, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            QuestPdfConfiguration.EnsureInitialized();
            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(50);
                    page.DefaultTextStyle(style => style.FontSize(10).FontFamily("Georgia"));
                    page.Header().Text("Contract review report").SemiBold().FontSize(16);
                    page.Content().PaddingVertical(10).Column(column =>
                    {
                        column.Spacing(6);
                        column.Item().Text(result.Document.FileName).Italic();
                        column.Item().Text($"Playbook: {result.Playbook.DisplayName}").FontSize(9);
                        column.Item().Background(Colors.Red.Lighten4).Padding(6)
                            .Text(AnalysisResult.Disclaimer).SemiBold().FontColor(Colors.Red.Darken2);
                        column.Item().Text($"{result.Findings.Count} findings across {result.Clauses.Count} clauses.");
                        foreach (var finding in result.Findings)
                        {
                            column.Item().PaddingTop(6).Text($"[{finding.Severity}] {finding.Title}").SemiBold();
                            column.Item().Text($"Category: {finding.Category}").FontSize(9);
                            if (!string.IsNullOrWhiteSpace(finding.Quote))
                            {
                                column.Item().Text($"\"{finding.Quote}\"").Italic().FontSize(9);
                            }

                            column.Item().Text(finding.Explanation);
                            if (!string.IsNullOrWhiteSpace(finding.SuggestedFix))
                            {
                                column.Item().Text($"Suggested fix: {finding.SuggestedFix}").FontSize(9);
                            }
                        }
                    });
                    page.Footer().AlignCenter().Text(AnalysisResult.Disclaimer).FontSize(8);
                });
            }).GeneratePdf(outputPath);

            return outputPath;
        }, cancellationToken);

    private static Paragraph Heading(string text, int halfPoints, bool bold) => new(
        new ParagraphProperties(new SpacingBetweenLines { Before = "240", After = "80" }),
        new Run(new RunProperties(new Bold { Val = bold }, new FontSize { Val = halfPoints.ToString(CultureInfo.InvariantCulture) }),
            new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph ParagraphOf(string text, bool italic = false) => new(
        new Run(
            new RunProperties(italic ? new Italic() : new Italic { Val = false }),
            new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph Disclaimer() => new(
        new ParagraphProperties(new SpacingBetweenLines { Before = "120", After = "120" }),
        new Run(new RunProperties(new Bold(), new WordColor { Val = "B91C1C" }),
            new Text(AnalysisResult.Disclaimer)));
}
