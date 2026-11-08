using DraftEngine.Analysis;

namespace DraftEngine.Analysis.Tests;

public sealed class AiReviewTests : IDisposable
{
    private readonly string _directory = Fixtures.TempDirectory();

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

    private sealed class FakeReviewer : IAiReviewer
    {
        public int Calls { get; private set; }
        public IReadOnlyList<AiFindingCandidate> Candidates { get; init; } = [];
        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<AiFindingCandidate>> ReviewAsync(AiReviewRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Failure is not null
                ? Task.FromException<IReadOnlyList<AiFindingCandidate>>(Failure)
                : Task.FromResult(Candidates);
        }
    }

    private async Task<AnalysisResult> AnalyzeAsync(
        IAiReviewer reviewer,
        bool useAi = true,
        bool consent = true,
        string[]? paragraphs = null)
    {
        var path = Fixtures.CreateDocx(_directory, paragraphs ?? Fixtures.GoodMutualNdaParagraphs);
        var document = await DocumentImporter.ImportAsync(path);
        return await new ContractAnalyzer(reviewer).AnalyzeAsync(document, new AnalysisOptions
        {
            ContractType = PlaybookCatalog.MutualNda,
            UseAi = useAi,
            AiConsentGranted = consent
        });
    }

    private const string GroundedQuote =
        "Each party shall protect the other party's confidential information using reasonable care.";

    [Fact]
    public async Task VerbatimAiQuote_IsAccepted()
    {
        var reviewer = new FakeReviewer
        {
            Candidates =
            [
                new AiFindingCandidate
                {
                    Id = "1",
                    Severity = FindingSeverity.High,
                    Category = "Confidentiality",
                    Quote = GroundedQuote,
                    Explanation = "The standard of care is vague.",
                    SuggestedFix = "Specify reasonable care and permitted disclosures."
                }
            ]
        };

        var result = await AnalyzeAsync(reviewer);
        var finding = Assert.Single(result.Findings, item => item.IsAiGenerated);

        Assert.True(result.Statistics.AiUsed);
        Assert.Equal(1, result.Statistics.AiFindingsAccepted);
        Assert.Equal(0, result.Statistics.AiFindingsDiscarded);
        Assert.Contains(GroundedQuote, result.Document.Text, StringComparison.Ordinal);
        Assert.Equal(0, result.Statistics.AiError is null ? 0 : 1);
    }

    [Fact]
    public async Task HallucinatedQuote_IsDiscarded()
    {
        var reviewer = new FakeReviewer
        {
            Candidates =
            [
                new AiFindingCandidate
                {
                    Id = "2",
                    Severity = FindingSeverity.Critical,
                    Category = "Liability",
                    Quote = "The parties waive all rights and accept unlimited liability.",
                    Explanation = "This clause is dangerous.",
                    SuggestedFix = "Delete it."
                }
            ]
        };

        var result = await AnalyzeAsync(reviewer);

        Assert.DoesNotContain(result.Findings, finding => finding.IsAiGenerated);
        Assert.Equal(0, result.Statistics.AiFindingsAccepted);
        Assert.Equal(1, result.Statistics.AiFindingsDiscarded);
    }

    [Fact]
    public async Task CitationLikeContent_IsRemovedFromAiOutput()
    {
        var reviewer = new FakeReviewer
        {
            Candidates =
            [
                new AiFindingCandidate
                {
                    Id = "3",
                    Severity = FindingSeverity.Medium,
                    Category = "Confidentiality",
                    Quote = GroundedQuote,
                    Explanation = "See Smith v. Jones, 123 F.3d 4. The standard of care is vague.",
                    SuggestedFix = "Add a mutual carve-out. See 17 U.S.C. 101."
                }
            ]
        };

        var result = await AnalyzeAsync(reviewer);
        var finding = Assert.Single(result.Findings, item => item.IsAiGenerated);

        Assert.DoesNotContain("Smith v.", finding.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("F.3d", finding.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("U.S.C.", finding.SuggestedFix, StringComparison.OrdinalIgnoreCase);
        Assert.False(AiFindingGrounder.ContainsCitation(finding.Explanation));
        Assert.False(AiFindingGrounder.ContainsCitation(finding.SuggestedFix));
    }

    [Fact]
    public async Task WithoutConsent_NoAiCallIsMade()
    {
        var reviewer = new FakeReviewer();
        var result = await AnalyzeAsync(reviewer, useAi: true, consent: false);

        Assert.Equal(0, reviewer.Calls);
        Assert.False(result.Statistics.AiUsed);
    }

    [Fact]
    public async Task WithAiOff_NoAiCallIsMade()
    {
        var reviewer = new FakeReviewer();
        var result = await AnalyzeAsync(reviewer, useAi: false, consent: true, paragraphs: Fixtures.MissingTerm);

        Assert.Equal(0, reviewer.Calls);
        Assert.False(result.Statistics.AiUsed);
        Assert.NotEmpty(result.Findings);
    }

    [Fact]
    public async Task AiFailure_DoesNotHideDeterministicFindings()
    {
        var reviewer = new FakeReviewer { Failure = new InvalidOperationException("network unavailable") };
        var result = await AnalyzeAsync(reviewer, paragraphs: Fixtures.MissingTerm);

        Assert.NotNull(result.Statistics.AiError);
        Assert.False(result.Statistics.AiUsed);
        Assert.NotEmpty(result.Findings);
    }

    [Fact]
    public void Prompt_ForbidsCitationsAndQuotesMustBeVerbatim()
    {
        Assert.Contains("NEVER cite", AiReviewPrompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("verbatim", AiReviewPrompt.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }
}
