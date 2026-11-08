using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

namespace DraftEngine.Analysis.Tests;

/// <summary>Synthetic contracts and file builders used by the analysis tests.</summary>
public static class Fixtures
{
    public static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "draftengine-analysis-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string CreateDocx(string directory, params string[] paragraphs)
    {
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.docx");
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new WordDocument(new Body());
        foreach (var text in paragraphs)
        {
            main.Document.Body!.Append(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
        }

        main.Document.Save();
        return path;
    }

    public static string CreateText(string directory, params string[] paragraphs)
    {
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, string.Join(Environment.NewLine, paragraphs));
        return path;
    }

    public static string CreatePdf(string directory, params string[] paragraphs)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.pdf");
        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(36);
                page.DefaultTextStyle(style => style.FontSize(11));
                page.Content().Column(column =>
                {
                    foreach (var text in paragraphs)
                    {
                        column.Item().Text(text);
                    }
                });
            });
        }).GeneratePdf(path);
        return path;
    }

    public const string GoodMutualNda =
        "MUTUAL NON-DISCLOSURE AGREEMENT";
    public static readonly string[] GoodMutualNdaParagraphs =
    [
        "MUTUAL NON-DISCLOSURE AGREEMENT",
        "1. Confidentiality",
        "Each party shall protect the other party's confidential information using reasonable care.",
        "2. Term",
        "This agreement remains in effect for two years after disclosure.",
        "3. Governing Law",
        "This agreement is governed by the laws of the State of California.",
        "4. Return of Materials",
        "Upon request each party shall return or destroy all confidential materials.",
        "5. Remedies",
        "The parties acknowledge that injunctive relief and equitable relief are available for breach."
    ];

    public static readonly string[] MissingTerm =
    [
        "MUTUAL NON-DISCLOSURE AGREEMENT",
        "1. Confidentiality",
        "Each party shall protect the other party's confidential information.",
        "2. Governing Law",
        "This agreement is governed by the laws of the State of New York.",
        "3. Return of Materials",
        "Upon request each party shall return or destroy all confidential materials.",
        "4. Remedies",
        "Injunctive relief and equitable relief are available."
    ];

    public static readonly string[] MissingGoverningLaw =
    [
        "MUTUAL NON-DISCLOSURE AGREEMENT",
        "1. Confidentiality",
        "Each party shall protect confidential information.",
        "2. Term",
        "The term is three years.",
        "3. Return of Materials",
        "Each party shall return or destroy confidential materials.",
        "4. Remedies",
        "Injunctive relief is available."
    ];

    public static readonly string[] MissingConfidentiality =
    [
        "MUTUAL NON-DISCLOSURE AGREEMENT",
        "1. Term",
        "The term is two years.",
        "2. Governing Law",
        "This agreement is governed by the laws of Delaware.",
        "3. Return of Materials",
        "Each party shall return or destroy materials.",
        "4. Remedies",
        "Injunctive relief is available."
    ];

    public static readonly string[] MissingReturnAndRemedies =
    [
        "MUTUAL NON-DISCLOSURE AGREEMENT",
        "1. Confidentiality",
        "Each party shall protect the other party's confidential information.",
        "2. Term",
        "This agreement remains in effect for two years.",
        "3. Governing Law",
        "This agreement is governed by California law."
    ];

    public static readonly string[] OneWayMismatch =
    [
        "ONE-WAY NON-DISCLOSURE AGREEMENT",
        "1. Confidentiality",
        "The recipient shall protect the disclosing party's confidential information.",
        "2. Term",
        "The confidentiality period is two years.",
        "3. Governing Law",
        "This agreement is governed by the laws of Texas."
    ];

    public static readonly string[] OneSidedIndemnity =
    [
        "MUTUAL NON-DISCLOSURE AGREEMENT",
        "1. Confidentiality",
        "Each party shall protect confidential information.",
        "2. Term",
        "The term is two years.",
        "3. Governing Law",
        "This agreement is governed by California law.",
        "4. Return of Materials",
        "Each party shall return or destroy materials.",
        "5. Remedies",
        "Injunctive relief is available.",
        "6. Indemnification",
        "The recipient shall indemnify only the disclosing party for any breach."
    ];

    public static readonly string[] UnlimitedLiability =
    [
        "MUTUAL NON-DISCLOSURE AGREEMENT",
        "1. Confidentiality",
        "Each party shall protect confidential information.",
        "2. Term",
        "The term is two years.",
        "3. Governing Law",
        "This agreement is governed by California law.",
        "4. Return of Materials",
        "Each party shall return or destroy materials.",
        "5. Remedies",
        "Injunctive relief is available.",
        "6. Liability",
        "The parties accept unlimited liability for any breach."
    ];

    public static readonly string[] PerpetualConfidentiality =
    [
        "MUTUAL NON-DISCLOSURE AGREEMENT",
        "1. Confidentiality",
        "Each party shall protect confidential information in perpetuity.",
        "2. Term",
        "The term is two years.",
        "3. Governing Law",
        "This agreement is governed by California law.",
        "4. Return of Materials",
        "Each party shall return or destroy materials.",
        "5. Remedies",
        "Injunctive relief is available."
    ];

    public static readonly string[] BlankPlaceholders =
    [
        "MUTUAL NON-DISCLOSURE AGREEMENT",
        "1. Confidentiality",
        "The party named [•] shall protect confidential information.",
        "2. Term",
        "The term is ____ months.",
        "3. Governing Law",
        "This agreement is governed by {{GoverningLaw}}.",
        "4. Return of Materials",
        "Each party shall return or destroy materials.",
        "5. Remedies",
        "Injunctive relief is available."
    ];
}
