using System.Diagnostics;
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
    private const int HeadingFontSizeHalfPoints = 24; // 12pt.
    private const int TitleFontSizeHalfPoints = 56; // 28pt.
    private const int FooterFontSizeHalfPoints = 18; // 9pt.
    private const uint UsLetterWidthTwips = 12240U;
    private const uint UsLetterHeightTwips = 15840U;
    private const int PageMarginTwips = 1440;
    private const uint HeaderFooterTwips = 720U;

    public async Task<string> BuildDocxAsync(Contract contract, string fullContractText, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(fullContractText);
        ArgumentNullException.ThrowIfNull(outputPath);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path must not be empty.", nameof(outputPath));
        }

        var title = DocumentDefaults.NormalizeTitle(contract.Title);
        var segments = ContractTextParser.Parse(fullContractText);
        var parties = ContractParties.ToDisplayText(contract.PartiesJson);
        var signatures = ContractParties.GetSignatureNames(contract);

        if (string.IsNullOrWhiteSpace(fullContractText))
        {
            // Keep exporting a safe title/signature document instead of failing the user's export.
            Trace.TraceWarning(
                "Contract text is empty; generating a DOCX with the title page and signature block only.");
        }

        try
        {
            await Task.Run(() =>
                DocumentFileWriter.WriteAtomically(outputPath, tempPath =>
                {
                    WriteDocument(contract, title, segments, parties, signatures, tempPath);
                    DocumentFileWriter.ValidateDocx(tempPath);
                }));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to generate the DOCX document '{title}' at '{outputPath}'. " +
                "No existing file was modified.",
                ex);
        }

        return outputPath;
    }

    private static void WriteDocument(
        Contract contract,
        string title,
        IReadOnlyList<ContractSegment> segments,
        string parties,
        SignatureNames signatures,
        string outputPath)
    {
        using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        AppendTitlePage(body, contract, title, parties);
        body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));

        AppendContractBody(body, segments);
        AppendSignatureBlock(body, signatures);

        AppendFooter(mainPart, body);
        mainPart.Document.Save();
    }

    private static void AppendTitlePage(Body body, Contract contract, string title, string parties)
    {
        var titleParagraph = new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { After = "600" }),
            NewRun(title, TitleFontSizeHalfPoints, bold: true));
        body.AppendChild(titleParagraph);

        AppendDetail(body, "Parties", parties);
        AppendDetail(body, "Client", contract.ClientName);
        AppendDetail(body, "Email", contract.ClientEmail);
        AppendDetail(body, "Effective Date", DocumentDefaults.FormatEffectiveDate(contract.EffectiveDate));
        AppendDetail(body, "Governing Law", DocumentDefaults.OrNotApplicable(contract.GoverningLaw));
        AppendDetail(body, "Term", DocumentDefaults.FormatTerm(contract.TermMonths));
        AppendDetail(body, "Contract Type", DocumentDefaults.OrNotApplicable(contract.ContractType));
    }

    private static void AppendDetail(Body body, string label, string value)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = "120" }),
            NewRun($"{label}: ", BodyFontSizeHalfPoints, bold: true),
            NewRun(value, BodyFontSizeHalfPoints)));
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
                    NewRun(segment.Text, HeadingFontSizeHalfPoints, bold: true)));
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

    private static void AppendSignatureBlock(Body body, SignatureNames signatures)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "600" }),
            NewRun(DocumentDefaults.WitnessClause, BodyFontSizeHalfPoints)));

        AppendSignatureLine(body, signatures.LeftName, signatures.RightName);
        AppendSignatureLine(body, "Date", "Date");
    }

    private static void AppendSignatureLine(Body body, string leftLabel, string rightLabel)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "480", After = "60" }),
            NewRun("________________________________", BodyFontSizeHalfPoints),
            NewRun(new string(' ', 12), BodyFontSizeHalfPoints),
            NewRun("________________________________", BodyFontSizeHalfPoints)));
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = "120" }),
            NewRun(leftLabel, BodyFontSizeHalfPoints),
            NewRun(new string(' ', 12), BodyFontSizeHalfPoints),
            NewRun(rightLabel, BodyFontSizeHalfPoints)));
    }

    private static void AppendFooter(MainDocumentPart mainPart, Body body)
    {
        var footerPart = mainPart.AddNewPart<FooterPart>();
        var footerParagraph = new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }));

        footerParagraph.Append(NewRun("Page ", FooterFontSizeHalfPoints));
        AppendField(footerParagraph, "PAGE", "1");
        footerParagraph.Append(NewRun(" of ", FooterFontSizeHalfPoints));
        AppendField(footerParagraph, "NUMPAGES", "1");

        footerPart.Footer = new Footer(footerParagraph);
        footerPart.Footer.Save();

        var sectionProperties = new SectionProperties(
            new FooterReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(footerPart) },
            new PageSize { Width = UsLetterWidthTwips, Height = UsLetterHeightTwips },
            new PageMargin
            {
                Top = PageMarginTwips,
                Bottom = PageMarginTwips,
                Left = PageMarginTwips,
                Right = PageMarginTwips,
                Header = HeaderFooterTwips,
                Footer = HeaderFooterTwips
            });
        // Section properties must be the last child of the body; the body is handed in
        // by WriteDocument (MainDocumentPart.Document is nullable in OpenXml 3.5+).
        body.AppendChild(sectionProperties);
    }

    private static void AppendField(Paragraph paragraph, string fieldCode, string placeholder)
    {
        paragraph.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }));
        paragraph.Append(new Run(new FieldCode(fieldCode) { Space = SpaceProcessingModeValues.Preserve }));
        paragraph.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));
        paragraph.Append(NewRun(placeholder, FooterFontSizeHalfPoints));
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
