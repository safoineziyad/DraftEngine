using System.Diagnostics;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DraftEngine.Documents.Builders;

/// <summary>
/// PDF document builder using QuestPDF. Renders the same structure as <see cref="DocxBuilder"/>:
/// title page, details block, numbered clause headings, justified Georgia body text,
/// signature block, and a centered "page X of Y" footer.
/// </summary>
public class PdfBuilder : IPdfBuilder
{
    private const float PageMarginPoints = 72;
    private const float BodyFontSize = 11;
    private const float BodyLineHeight = 1.15f;
    private const float TitleFontSize = 28;
    private const float PartiesFontSize = 12;
    private const float HeadingFontSize = 12;
    private const float FooterFontSize = 9;
    private const float TitleTopPadding = 140;
    private const float SignatureLineHeight = 24;

    public async Task<string> BuildPdfAsync(Contract contract, string fullContractText, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(fullContractText);
        ArgumentNullException.ThrowIfNull(outputPath);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path must not be empty.", nameof(outputPath));
        }

        // Configure QuestPDF once, before any document is generated.
        QuestPdfConfiguration.EnsureInitialized();

        var title = DocumentDefaults.NormalizeTitle(contract.Title);
        var segments = ContractTextParser.Parse(fullContractText);
        var parties = ContractParties.ToDisplayText(contract.PartiesJson);
        var signatures = ContractParties.GetSignatureNames(contract);

        if (string.IsNullOrWhiteSpace(fullContractText))
        {
            // Keep exporting a safe title/signature document instead of failing the user's export.
            Trace.TraceWarning(
                "Contract text is empty; generating a PDF with the title page and signature block only.");
        }

        try
        {
            await Task.Run(() =>
                DocumentFileWriter.WriteAtomically(outputPath, tempPath =>
                {
                    Document.Create(container => ComposeDocument(container, contract, title, segments, parties, signatures))
                        .WithMetadata(new DocumentMetadata
                        {
                            Title = title,
                            Author = "DraftEngine",
                            Subject = contract.ContractType ?? string.Empty
                        })
                        .GeneratePdf(tempPath);

                    DocumentFileWriter.ValidatePdf(tempPath);
                }));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to generate the PDF document '{title}' at '{outputPath}'. " +
                "No existing file was modified.",
                ex);
        }

        return outputPath;
    }

    private static void ComposeDocument(
        IDocumentContainer container,
        Contract contract,
        string title,
        IReadOnlyList<ContractSegment> segments,
        string parties,
        SignatureNames signatures)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginTop(PageMarginPoints);
            page.MarginBottom(PageMarginPoints);
            page.MarginLeft(PageMarginPoints);
            page.MarginRight(PageMarginPoints);
            page.DefaultTextStyle(text => text
                .FontFamily("Georgia")
                .FontSize(BodyFontSize)
                .LineHeight(BodyLineHeight));

            page.Content().Column(column =>
            {
                column.Item().PaddingTop(TitleTopPadding).AlignCenter().Text(title).FontSize(TitleFontSize).Bold();
                column.Item().PaddingTop(24).AlignCenter().Text(parties).FontSize(PartiesFontSize);

                column.Item().PaddingTop(48).Column(details =>
                {
                    AppendDetail(details, "Client", contract.ClientName);
                    AppendDetail(details, "Email", contract.ClientEmail);
                    AppendDetail(details, "Effective Date", DocumentDefaults.FormatEffectiveDate(contract.EffectiveDate));
                    AppendDetail(details, "Governing Law", DocumentDefaults.OrNotApplicable(contract.GoverningLaw));
                    AppendDetail(details, "Term", DocumentDefaults.FormatTerm(contract.TermMonths));
                    AppendDetail(details, "Contract Type", DocumentDefaults.OrNotApplicable(contract.ContractType));
                });

                column.Item().PageBreak();

                foreach (var segment in segments)
                {
                    if (segment.IsHeading)
                    {
                        column.Item().PaddingTop(14).Text(segment.Text).Bold().FontSize(HeadingFontSize);
                    }
                    else
                    {
                        column.Item().PaddingBottom(6).Text(segment.Text).Justify();
                    }
                }

                column.Item().PaddingTop(36).Text(DocumentDefaults.WitnessClause);
                column.Item().PaddingTop(24).Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Height(SignatureLineHeight).BorderBottom(1).PaddingBottom(2);
                        left.Item().PaddingTop(4).Text(signatures.LeftName);
                    });
                    row.Spacing(24);
                    row.RelativeItem().Column(right =>
                    {
                        right.Item().Height(SignatureLineHeight).BorderBottom(1).PaddingBottom(2);
                        right.Item().PaddingTop(4).Text(signatures.RightName);
                    });
                });
                column.Item().PaddingTop(16).Row(row =>
                {
                    row.RelativeItem().Text("Date: ______________________");
                    row.Spacing(24);
                    row.RelativeItem().Text("Date: ______________________");
                });
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("Page ").FontSize(FooterFontSize);
                text.CurrentPageNumber().FontSize(FooterFontSize);
                text.Span(" of ").FontSize(FooterFontSize);
                text.TotalPages().FontSize(FooterFontSize);
            });
        });
    }

    private static void AppendDetail(ColumnDescriptor details, string label, string value)
    {
        details.Item().PaddingBottom(6).Text(text =>
        {
            text.Span($"{label}: ").SemiBold();
            text.Span(value);
        });
    }
}
