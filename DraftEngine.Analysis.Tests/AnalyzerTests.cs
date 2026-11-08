using DraftEngine.Analysis;

namespace DraftEngine.Analysis.Tests;

public sealed class AnalyzerTests : IDisposable
{
    private readonly string _directory = Fixtures.TempDirectory();
    private readonly ContractAnalyzer _analyzer = new();

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

    private async Task<AnalysisResult> AnalyzeAsync(
        string[] paragraphs,
        string contractType = PlaybookCatalog.MutualNda,
        AnalysisOptions? options = null,
        IAiReviewer? reviewer = null)
    {
        var path = Fixtures.CreateDocx(_directory, paragraphs);
        var document = await DocumentImporter.ImportAsync(path);
        var analyzer = new ContractAnalyzer(reviewer);
        return await analyzer.AnalyzeAsync(document, options ?? new AnalysisOptions { ContractType = contractType });
    }

    [Fact]
    public async Task DocxImporter_PreservesParagraphOrderAndOffsets()
    {
        var path = Fixtures.CreateDocx(_directory, "TITLE", "1. Confidentiality", "Some body text.");
        var document = await DocumentImporter.ImportAsync(path);

        Assert.Equal(DocumentFormat.Docx, document.Format);
        Assert.Equal(3, document.Paragraphs.Count);
        Assert.Equal("TITLE", document.Paragraphs[0].Text);
        Assert.Equal(0, document.Paragraphs[0].StartOffset);
        Assert.Equal(document.Paragraphs[0].EndOffset + 1, document.Paragraphs[1].StartOffset);
        Assert.True(document.Paragraphs[1].IsHeading);
        Assert.Contains("1. Confidentiality", document.Text);
    }

    [Fact]
    public async Task PdfImporter_ExtractsTextLayer()
    {
        var path = Fixtures.CreatePdf(_directory, "MUTUAL NON-DISCLOSURE AGREEMENT", "1. Confidentiality", "Each party shall protect confidential information.");
        var document = await DocumentImporter.ImportAsync(path);

        Assert.Equal(DocumentFormat.Pdf, document.Format);
        Assert.True(document.HasTextLayer);
        Assert.Contains("Confidentiality", document.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScannedPdf_IsReportedAsMissingTextLayer()
    {
        var path = Fixtures.CreatePdf(_directory);
        var document = await DocumentImporter.ImportAsync(path);
        Assert.False(document.HasTextLayer);

        var result = await new ContractAnalyzer().AnalyzeAsync(document, new AnalysisOptions());
        Assert.Contains(result.Findings, finding => finding.RuleId == "ocr.unsupported");
        Assert.Empty(result.Clauses);
    }

    [Fact]
    public async Task Segmenter_SplitsNumberedClausesAndInfersCategories()
    {
        var path = Fixtures.CreateDocx(_directory, Fixtures.GoodMutualNdaParagraphs);
        var document = await DocumentImporter.ImportAsync(path);
        var clauses = ClauseSegmenter.Segment(document);

        Assert.True(clauses.Count >= 5);
        Assert.True(clauses.Any(clause => clause.Category == "Confidentiality"),
            string.Join(" | ", clauses.Select(clause => $"{clause.Id}:{clause.Category}:{clause.Heading}")));
        Assert.Contains(clauses, clause => clause.Category == "Term");
        Assert.Contains(clauses, clause => clause.Category == "Governing Law");
        Assert.Contains(clauses, clause => clause.Category == "Return of Materials");
        Assert.Equal(clauses.Count, clauses.Select(clause => clause.Id).Distinct().Count());
    }

    [Fact]
    public async Task GoodNda_HasNoMissingRequiredClauseFindings()
    {
        var result = await AnalyzeAsync(Fixtures.GoodMutualNdaParagraphs);

        Assert.DoesNotContain(result.Findings, finding =>
            finding.RuleId.StartsWith("nda.", StringComparison.Ordinal) &&
            finding.Title.StartsWith("Missing", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.Statistics.AiUsed);
        Assert.Equal("Not legal advice. Review by a licensed attorney required.", AnalysisResult.Disclaimer);
    }

    [Fact]
    public async Task MissingTerm_IsReported()
    {
        var result = await AnalyzeAsync(Fixtures.MissingTerm);
        Assert.Contains(result.Findings, finding => finding.RuleId == "nda.term");
    }

    [Fact]
    public async Task MissingGoverningLaw_IsReportedBothAsClauseAndFact()
    {
        var result = await AnalyzeAsync(Fixtures.MissingGoverningLaw);
        Assert.Contains(result.Findings, finding => finding.RuleId == "nda.governing-law");
        Assert.Contains(result.Findings, finding => finding.RuleId == "nda.fact.governing-law");
    }

    [Fact]
    public async Task MissingConfidentiality_IsCritical()
    {
        var result = await AnalyzeAsync(Fixtures.MissingConfidentiality);
        var finding = Assert.Single(result.Findings, item => item.RuleId == "nda.confidentiality");
        Assert.Equal(FindingSeverity.Critical, finding.Severity);
    }

    [Fact]
    public async Task Placeholders_AreFoundVerbatim()
    {
        var result = await AnalyzeAsync(Fixtures.BlankPlaceholders);
        var quotes = result.Findings.Where(finding => finding.RuleId == "placeholder").Select(finding => finding.Quote).ToList();

        Assert.Contains("[•]", quotes);
        Assert.Contains("____", quotes);
        Assert.Contains("{{GoverningLaw}}", quotes);
        Assert.All(result.Findings.Where(finding => finding.RuleId == "placeholder"),
            finding => Assert.Contains(finding.Quote, result.Document.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RedFlag_UnlimitedLiability_IsHigh()
    {
        var result = await AnalyzeAsync(Fixtures.UnlimitedLiability);
        var finding = Assert.Single(result.Findings, item => item.RuleId == "nda.flag.unlimited-liability");
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Contains("unlimited liability", finding.Quote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RedFlag_PerpetualAndOneSidedIndemnity_AreDetected()
    {
        var perpetual = await AnalyzeAsync(Fixtures.PerpetualConfidentiality);
        Assert.Contains(perpetual.Findings, finding => finding.RuleId == "nda.flag.perpetual");

        var indemnity = await AnalyzeAsync(Fixtures.OneSidedIndemnity);
        Assert.Contains(indemnity.Findings, finding => finding.RuleId == "nda.flag.one-sided-indemnity");
    }

    [Fact]
    public async Task OneWayDocument_ReviewedAgainstMutualPlaybook_ReportsMissingProtections()
    {
        var result = await AnalyzeAsync(Fixtures.OneWayMismatch);
        Assert.Contains(result.Findings, finding => finding.RuleId == "nda.return");
        Assert.Contains(result.Findings, finding => finding.RuleId == "nda.remedies");
    }

    [Fact]
    public async Task Facts_ExtractGoverningLawAndTerm()
    {
        var result = await AnalyzeAsync(Fixtures.GoodMutualNdaParagraphs);
        var governingLaw = Assert.Single(result.Facts, fact => fact.Name == "GoverningLaw");
        var term = Assert.Single(result.Facts, fact => fact.Name == "Term");

        Assert.True(governingLaw.Found, $"Governing law not found in: {result.Document.Text}");
        Assert.True(term.Found, $"Term not found in: {result.Document.Text}");
        Assert.Contains("two years", term.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeterministicPrecisionAndRecall_MeetThresholds()
    {
        var fixtures = new (string[] Paragraphs, string[] Expected)[]
        {
            (Fixtures.GoodMutualNdaParagraphs, []),
            (Fixtures.MissingTerm, ["nda.term"]),
            (Fixtures.MissingGoverningLaw, ["nda.governing-law", "nda.fact.governing-law"]),
            (Fixtures.MissingConfidentiality, ["nda.confidentiality"]),
            (Fixtures.MissingReturnAndRemedies, ["nda.return", "nda.remedies"]),
            (Fixtures.OneWayMismatch, ["nda.return", "nda.remedies"]),
            (Fixtures.OneSidedIndemnity, []),
            (Fixtures.UnlimitedLiability, []),
            (Fixtures.PerpetualConfidentiality, []),
            (Fixtures.BlankPlaceholders, [])
        };

        var requiredRuleIds = PlaybookCatalog.Get(PlaybookCatalog.MutualNda).Rules
            .Where(rule => rule.Kind is PlaybookRuleKind.RequiredClause or PlaybookRuleKind.RequiredFact)
            .Select(rule => rule.Id)
            .ToHashSet(StringComparer.Ordinal);

        var truePositives = 0;
        var actual = 0;
        var expectedTotal = 0;

        foreach (var (paragraphs, expected) in fixtures)
        {
            var result = await AnalyzeAsync(paragraphs);
            var reported = result.Findings
                .Select(finding => finding.RuleId)
                .Where(requiredRuleIds.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

            actual += reported.Count;
            expectedTotal += expected.Length;
            truePositives += expected.Count(reported.Contains);
            Assert.All(expected, ruleId => Assert.Contains(ruleId, reported));
        }

        var precision = actual == 0 ? 1 : (double)truePositives / actual;
        var recall = expectedTotal == 0 ? 1 : (double)truePositives / expectedTotal;

        Assert.True(precision >= 0.9, $"Precision was {precision:P0}.");
        Assert.True(recall >= 0.8, $"Recall was {recall:P0}.");
    }

    [Fact]
    public async Task Playbooks_ExistForAllFourContractTypes()
    {
        Assert.Equal(4, PlaybookCatalog.All.Count);
        foreach (var type in new[]
                 {
                     PlaybookCatalog.MutualNda, PlaybookCatalog.OneWayNda,
                     PlaybookCatalog.IndependentContractorAgreement, PlaybookCatalog.ConsultingAgreement
                 })
        {
            Assert.NotEmpty(PlaybookCatalog.Get(type).Rules);
        }
    }

    [Fact]
    public async Task Comparison_DetectsAddedModifiedAndRemovedClauses()
    {
        var oldPath = Fixtures.CreateDocx(_directory, Fixtures.GoodMutualNdaParagraphs);
        var newPath = Fixtures.CreateDocx(_directory,
            "MUTUAL NON-DISCLOSURE AGREEMENT",
            "1. Confidentiality",
            "Each party shall protect the other party's confidential information using reasonable care.",
            "2. Term",
            "This agreement remains in effect for five years after disclosure.",
            "3. Governing Law",
            "This agreement is governed by the laws of the State of California.",
            "4. Return of Materials",
            "Upon request each party shall return or destroy all confidential materials.",
            "5. Remedies",
            "The parties acknowledge that injunctive relief and equitable relief are available for breach.",
            "6. Notices",
            "Notices must be in writing and delivered by email.");

        var comparison = DocumentComparison.Compare(
            await DocumentImporter.ImportAsync(oldPath),
            await DocumentImporter.ImportAsync(newPath));

        Assert.Equal(1, comparison.AddedCount);
        Assert.Equal(0, comparison.RemovedCount);
        Assert.True(comparison.ModifiedCount >= 1);
        Assert.Contains(comparison.Changes, change => change.Kind == ClauseChangeKind.Added && change.Heading.Contains("Notices"));
    }
}
