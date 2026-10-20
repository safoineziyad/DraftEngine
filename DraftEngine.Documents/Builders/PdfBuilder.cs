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
    static PdfBuilder()
    {
        // QuestPDF requires a license to be set once, before the first document is generated.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<string> BuildPdfAsync(Contract contract, string fullContractText, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(fullContractText);
        ArgumentNullException.ThrowIfNull(outputPath);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path must not be empty.", nameof(outputPath));
        }

        var segments = ContractTextParser.Parse(fullContractText);
        var parties = ContractParties.ToDisplayText(contract.PartiesJson);
        var signatureNames = ContractParties.GetNames(contract.PartiesJson);

        await Task.Run(() =>
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Document.Create(container => ComposeDocument(container, contract, segments, parties, signatureNames))
                .WithMetadata(new DocumentMetadata
                {
                    Title = contract.Title,
                    Author = "DraftEngine",
                    Subject = contract.ContractType
                })
                .GeneratePdf(outputPath);
        });
        return outputPath;
    }

    private static void ComposeDocument(
        IDocumentContainer container,
        Contract contract,
        IReadOnlyList<ContractSegment> segments,
        string parties,
        IReadOnlyList<string> signatureNames)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginTop(72);
            page.MarginBottom(72);
            page.MarginLeft(72);
            page.MarginRight(72);
            page.DefaultTextStyle(text => text.FontFamily("Georgia").FontSize(11).LineHeight(1.15f));

            page.Content().Column(column =>
            {
                column.Item().PaddingTop(140).AlignCenter().Text(contract.Title).FontSize(28).Bold();
                column.Item().PaddingTop(24).AlignCenter().Text(parties).FontSize(12);

                column.Item().PaddingTop(48).Column(details =>
                {
                    AppendDetail(details, "Client", contract.ClientName);
                    AppendDetail(details, "Email", contract.ClientEmail);
                    AppendDetail(details, "Effective Date", contract.EffectiveDate?.ToString("yyyy-MM-dd") ?? "N/A");
                    AppendDetail(details, "Governing Law", string.IsNullOrWhiteSpace(contract.GoverningLaw) ? "N/A" : contract.GoverningLaw);
                    AppendDetail(details, "Term", contract.TermMonths > 0 ? $"{contract.TermMonths} month(s)" : "N/A");
                    AppendDetail(details, "Contract Type", string.IsNullOrWhiteSpace(contract.ContractType) ? "N/A" : contract.ContractType);
                });

                column.Item().PageBreak();

                foreach (var segment in segments)
                {
                    if (segment.IsHeading)
                    {
                        column.Item().PaddingTop(14).Text(segment.Text).Bold().FontSize(12);
                    }
                    else
                    {
                        column.Item().PaddingBottom(6).Text(segment.Text).Justify();
                    }
                }

                var rightName = signatureNames.Count > 1 ? signatureNames[1] : "Counterparty";
                column.Item().PaddingTop(36).Text("IN WITNESS WHEREOF, the parties have executed this Agreement as of the Effective Date.");
                column.Item().PaddingTop(24).Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Height(24).BorderBottom(1).PaddingBottom(2);
                        left.Item().PaddingTop(4).Text(contract.ClientName);
                    });
                    row.Spacing(24);
                    row.RelativeItem().Column(right =>
                    {
                        right.Item().Height(24).BorderBottom(1).PaddingBottom(2);
                        right.Item().PaddingTop(4).Text(rightName);
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
                text.Span("Page ").FontSize(9);
                text.CurrentPageNumber().FontSize(9);
                text.Span(" of ").FontSize(9);
                text.TotalPages().FontSize(9);
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
