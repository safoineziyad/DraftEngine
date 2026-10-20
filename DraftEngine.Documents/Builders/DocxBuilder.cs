using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;

namespace DraftEngine.Documents.Builders;

/// <summary>
/// DOCX document builder using the Open XML SDK. Renders a title page, numbered clause
/// headings, justified Georgia body text, a signature block, and a "page X of Y" footer.
/// </summary>
public class DocxBuilder : IDocxBuilder
{
    private const string BodyFont = "Georgia";
    private const int BodyFontSizeHalfPoints = 22; // 11pt.

    public async Task<string> BuildDocxAsync(Contract contract, string fullContractText, string outputPath)
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

        await Task.Run(() => WriteDocument(contract, segments, parties, signatureNames, outputPath));
        return outputPath;
    }

    private static void WriteDocument(
        Contract contract,
        IReadOnlyList<ContractSegment> segments,
        string parties,
        IReadOnlyList<string> signatureNames,
        string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        AppendTitlePage(body, contract, parties);
        body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));

        AppendContractBody(body, segments);
        AppendSignatureBlock(body, contract, signatureNames);

        AppendFooter(mainPart);
        mainPart.Document.Save();
    }

    private static void AppendTitlePage(Body body, Contract contract, string parties)
    {
        var title = new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { After = "600" }),
            NewRun(contract.Title, 56, bold: true)); // 28pt.
        body.AppendChild(title);

        AppendDetail(body, "Parties", parties);
        AppendDetail(body, "Client", contract.ClientName);
        AppendDetail(body, "Email", contract.ClientEmail);
        AppendDetail(body, "Effective Date", contract.EffectiveDate?.ToString("yyyy-MM-dd") ?? "N/A");
        AppendDetail(body, "Governing Law", string.IsNullOrWhiteSpace(contract.GoverningLaw) ? "N/A" : contract.GoverningLaw);
        AppendDetail(body, "Term", contract.TermMonths > 0 ? $"{contract.TermMonths} month(s)" : "N/A");
        AppendDetail(body, "Contract Type", string.IsNullOrWhiteSpace(contract.ContractType) ? "N/A" : contract.ContractType);
    }

    private static void AppendDetail(Body body, string label, string value)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = "120" }),
            NewRun($"{label}: ", 22, bold: true),
            NewRun(value, 22)));
    }

    private static void AppendContractBody(Body body, IReadOnlyList<ContractSegment> segments)
    {
        foreach (var segment in segments)
        {
            if (segment.IsHeading)
            {
                body.AppendChild(new Paragraph(
                    new ParagraphProperties(
                        new SpacingBetweenLines { Before = "280", After = "120" },
                        new KeepNext()),
                    NewRun(segment.Text, 24, bold: true))); // 12pt heading.
            }
            else
            {
                body.AppendChild(new Paragraph(
                    new ParagraphProperties(
                        new Justification { Val = JustificationValues.Both },
                        new SpacingBetweenLines { After = "120", Line = "276", LineRule = LineSpacingRuleValues.Auto }),
                    NewRun(segment.Text, BodyFontSizeHalfPoints)));
            }
        }
    }

    private static void AppendSignatureBlock(Body body, Contract contract, IReadOnlyList<string> signatureNames)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "600" }),
            NewRun("IN WITNESS WHEREOF, the parties have executed this Agreement as of the Effective Date.", 22)));

        var rightName = signatureNames.Count > 1 ? signatureNames[1] : "Counterparty";
        AppendSignatureLine(body, contract.ClientName, rightName);
        AppendSignatureLine(body, "Date", "Date");
    }

    private static void AppendSignatureLine(Body body, string leftLabel, string rightLabel)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "480", After = "60" }),
            NewRun("________________________________", 22),
            NewRun(new string(' ', 12), 22),
            NewRun("________________________________", 22)));
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = "120" }),
            NewRun(leftLabel, 22),
            NewRun(new string(' ', 12), 22),
            NewRun(rightLabel, 22)));
    }

    private static void AppendFooter(MainDocumentPart mainPart)
    {
        var footerPart = mainPart.AddNewPart<FooterPart>();
        var footerParagraph = new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }));

        footerParagraph.Append(NewRun("Page ", 18));
        AppendField(footerParagraph, "PAGE", "1");
        footerParagraph.Append(NewRun(" of ", 18));
        AppendField(footerParagraph, "NUMPAGES", "1");

        footerPart.Footer = new Footer(footerParagraph);
        footerPart.Footer.Save();

        var sectionProperties = new SectionProperties(
            new FooterReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(footerPart) },
            new PageSize { Width = 12240U, Height = 15840U }, // US Letter.
            new PageMargin
            {
                Top = 1440,
                Bottom = 1440,
                Left = 1440,
                Right = 1440,
                Header = 720U,
                Footer = 720U
            });
        mainPart.Document.Body!.AppendChild(sectionProperties);
    }

    private static void AppendField(Paragraph paragraph, string fieldCode, string placeholder)
    {
        paragraph.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }));
        paragraph.Append(new Run(new FieldCode(fieldCode) { Space = SpaceProcessingModeValues.Preserve }));
        paragraph.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));
        paragraph.Append(NewRun(placeholder, 18));
        paragraph.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
    }

    private static Run NewRun(string text, int fontSizeHalfPoints, bool bold = false)
    {
        var runProperties = new RunProperties(
            new RunFonts { Ascii = BodyFont, HighAnsi = BodyFont, EastAsia = BodyFont, ComplexScript = BodyFont },
            new FontSize { Val = fontSizeHalfPoints.ToString() },
            new FontSizeComplexScript { Val = fontSizeHalfPoints.ToString() });
        if (bold)
        {
            runProperties.Append(new Bold());
            runProperties.Append(new BoldComplexScript());
        }

        return new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }
}
