using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DraftEngine.Analysis;

/// <summary>
/// Deterministic contract review plus optional grounded AI review. No network client is
/// created here; AI runs only when the caller supplies a reviewer and consent is true.
/// </summary>
public sealed class ContractAnalyzer
{
    private static readonly Regex PlaceholderPattern = new(
        @"\{\{[^}\n]{1,80}\}\}|\[(?:•|insert|specify|choose|tbd|date|name)[^\]\n]{0,60}\]|_{3,}|\bTBD\b|\bTODO\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GoverningLawFact = new(
        @"\bgoverning law\b[^.\n]{0,120}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TermFact = new(
        @"\b(?:term|duration)\b[^.]{0,120}?((?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\s*(?:year|month|day)s?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConfidentialityDurationFact = new(
        @"\bconfidential\w*\b[^.]{0,160}?((?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\s*(?:year|month|day)s?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PaymentFact = new(
        @"\b(?:payment|invoice|fees?)\b[^.]{0,140}?((?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\s*(?:day|month)s?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IAiReviewer? _aiReviewer;

    public ContractAnalyzer(IAiReviewer? aiReviewer = null)
    {
        _aiReviewer = aiReviewer;
    }

    public async Task<AnalysisResult> AnalyzeAsync(
        NormalizedDocument document,
        AnalysisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new AnalysisOptions();

        var stopwatch = Stopwatch.StartNew();
        var playbook = options.PlaybookOverride ?? PlaybookCatalog.Get(options.ContractType);
        var clauses = document.HasTextLayer ? ClauseSegmenter.Segment(document) : [];
        var findings = new List<AnalysisFinding>();

        if (!document.HasTextLayer)
        {
            findings.Add(new AnalysisFinding
            {
                Id = "ocr.unsupported",
                RuleId = "ocr.unsupported",
                Severity = FindingSeverity.Critical,
                Category = "Document",
                Title = "No text layer detected",
                Quote = string.Empty,
                Explanation = "This PDF appears to be scanned images. OCR is not supported yet, so the document could not be reviewed.",
                SuggestedFix = "Export a text-based PDF or a DOCX and analyze that file."
            });
        }
        else
        {
            AddRequiredClauseFindings(document, clauses, playbook, findings);
            AddRequiredFactFindings(document, playbook, findings);
            AddRedFlagFindings(document, playbook, findings);
            AddPlaceholderFindings(document, findings);
        }

        var facts = document.HasTextLayer
            ? ExtractFacts(document)
            : Array.Empty<ExtractedFact>();

        var aiUsed = false;
        var acceptedAi = 0;
        var discardedAi = 0;
        string? aiError = null;

        if (document.HasTextLayer && options.UseAi && options.AiConsentGranted && _aiReviewer is not null)
        {
            try
            {
                var request = new AiReviewRequest
                {
                    DocumentText = document.Text,
                    ContractType = playbook.ContractType,
                    PlaybookName = playbook.DisplayName,
                    RequiredCategories = playbook.Rules
                        .Where(rule => rule.Kind == PlaybookRuleKind.RequiredClause)
                        .Select(rule => rule.Category)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
                var candidates = await _aiReviewer.ReviewAsync(request, cancellationToken);
                var (accepted, discarded) = AiFindingGrounder.Ground(candidates, document.Text);
                discardedAi = discarded;
                acceptedAi = accepted.Count;
                aiUsed = true;

                var index = 0;
                foreach (var candidate in accepted)
                {
                    findings.Add(new AnalysisFinding
                    {
                        Id = $"ai-{index++}",
                        RuleId = "ai.review",
                        Severity = candidate.Severity,
                        Category = candidate.Category,
                        Title = $"AI review: {candidate.Category}",
                        Quote = candidate.Quote,
                        Explanation = candidate.Explanation,
                        SuggestedFix = candidate.SuggestedFix,
                        StartOffset = FindOffset(document.Text, candidate.Quote),
                        IsAiGenerated = true
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // A review failure must never hide the deterministic findings, and no exception
                // detail (which could include a response body) is surfaced or logged.
                aiError = "AI review could not run. The deterministic findings below are unaffected.";
            }
        }

        stopwatch.Stop();
        return new AnalysisResult
        {
            Document = document,
            Clauses = clauses,
            Findings = findings
                .OrderByDescending(finding => finding.Severity)
                .ThenBy(finding => finding.Category, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Facts = facts,
            Playbook = playbook,
            Statistics = new AnalysisStatistics
            {
                ParagraphCount = document.Paragraphs.Count,
                ClauseCount = clauses.Count,
                FindingsCount = findings.Count,
                AiUsed = aiUsed,
                AiFindingsAccepted = acceptedAi,
                AiFindingsDiscarded = discardedAi,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                AiError = aiError
            }
        };
    }

    private static void AddRequiredClauseFindings(
        NormalizedDocument document,
        IReadOnlyList<AnalyzedClause> clauses,
        Playbook playbook,
        List<AnalysisFinding> findings)
    {
        foreach (var rule in playbook.Rules.Where(rule => rule.Kind == PlaybookRuleKind.RequiredClause))
        {
            var match = clauses.Where(clause => clause.Category != "Preamble").FirstOrDefault(clause =>
                clause.Category.Equals(rule.Category, StringComparison.OrdinalIgnoreCase) ||
                rule.Keywords.Any(keyword =>
                    clause.Heading.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    (clause.Text.Length > 0 &&
                     clause.Text[..Math.Min(400, clause.Text.Length)]
                         .Contains(keyword, StringComparison.OrdinalIgnoreCase))));

            if (match is not null)
            {
                continue;
            }

            findings.Add(new AnalysisFinding
            {
                Id = rule.Id,
                RuleId = rule.Id,
                Severity = rule.Severity,
                Category = rule.Category,
                Title = $"Missing {rule.Category.ToLowerInvariant()} clause",
                Quote = FirstParagraph(document),
                Explanation = rule.Description + " No clause covering this category was found.",
                SuggestedFix = rule.SuggestedClauseCategory is null
                    ? "Add a clause covering this category."
                    : $"Insert a {rule.SuggestedClauseCategory} clause from the clause library.",
                StartOffset = document.Paragraphs.Count > 0 ? document.Paragraphs[0].StartOffset : 0
            });
        }
    }

    private static void AddRequiredFactFindings(
        NormalizedDocument document,
        Playbook playbook,
        List<AnalysisFinding> findings)
    {
        foreach (var rule in playbook.Rules.Where(rule => rule.Kind == PlaybookRuleKind.RequiredFact))
        {
            if (rule.Keywords.Any(keyword => document.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            findings.Add(new AnalysisFinding
            {
                Id = rule.Id,
                RuleId = rule.Id,
                Severity = rule.Severity,
                Category = rule.Category,
                Title = $"Missing {rule.Category.ToLowerInvariant()}",
                Quote = FirstParagraph(document),
                Explanation = rule.Description,
                SuggestedFix = "Add an explicit clause stating this term.",
                StartOffset = document.Paragraphs.Count > 0 ? document.Paragraphs[0].StartOffset : 0
            });
        }
    }

    private static void AddRedFlagFindings(
        NormalizedDocument document,
        Playbook playbook,
        List<AnalysisFinding> findings)
    {
        foreach (var rule in playbook.Rules.Where(rule => rule.Kind == PlaybookRuleKind.RedFlag))
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
            {
                continue;
            }

            Regex regex;
            try
            {
                regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var hits = 0;
            foreach (Match match in regex.Matches(document.Text))
            {
                if (hits >= 5 || match.Length == 0)
                {
                    break;
                }

                var quote = match.Value.Trim();
                if (quote.Length == 0)
                {
                    continue;
                }

                hits++;
                findings.Add(new AnalysisFinding
                {
                    Id = $"{rule.Id}:{hits}",
                    RuleId = rule.Id,
                    Severity = rule.Severity,
                    Category = rule.Category,
                    Title = rule.Description,
                    Quote = quote.Length > 400 ? quote[..400] : quote,
                    Explanation = rule.Description,
                    SuggestedFix = "Review this wording and make the obligation mutual or bounded where appropriate.",
                    StartOffset = match.Index
                });
            }
        }
    }

    private static void AddPlaceholderFindings(NormalizedDocument document, List<AnalysisFinding> findings)
    {
        var seenOffsets = new HashSet<int>();
        foreach (Match match in PlaceholderPattern.Matches(document.Text))
        {
            if (!seenOffsets.Add(match.Index))
            {
                continue;
            }

            var isBrace = match.Value.Contains("{{", StringComparison.Ordinal);
            findings.Add(new AnalysisFinding
            {
                Id = $"placeholder:{match.Index}",
                RuleId = "placeholder",
                Severity = isBrace ? FindingSeverity.High : FindingSeverity.Medium,
                Category = "Completeness",
                Title = "Unresolved placeholder",
                Quote = match.Value,
                Explanation = "A blank field or template placeholder is still in the document.",
                SuggestedFix = "Replace the placeholder with the agreed value, or remove the field.",
                StartOffset = match.Index
            });
        }
    }

    private static IReadOnlyList<ExtractedFact> ExtractFacts(NormalizedDocument document)
    {
        var facts = new List<ExtractedFact>();
        facts.Add(FactFrom("GoverningLaw", document.Text, GoverningLawFact));
        facts.Add(FactFrom("Term", document.Text, TermFact));
        facts.Add(FactFrom("ConfidentialityDuration", document.Text, ConfidentialityDurationFact));
        facts.Add(FactFrom("PaymentTerms", document.Text, PaymentFact));
        return facts;
    }

    private static ExtractedFact FactFrom(string name, string text, Regex pattern)
    {
        var match = pattern.Match(text);
        if (!match.Success)
        {
            return new ExtractedFact(name, null, string.Empty, false);
        }

        var value = match.Groups.Count > 1 && match.Groups[1].Success
            ? match.Groups[1].Value.Trim()
            : match.Value.Trim();
        return new ExtractedFact(name, value, match.Value.Trim(), true);
    }

    private static string FirstParagraph(NormalizedDocument document) =>
        document.Paragraphs.Count > 0 ? document.Paragraphs[0].Text : string.Empty;

    private static int FindOffset(string text, string quote)
    {
        if (string.IsNullOrEmpty(quote))
        {
            return 0;
        }

        var index = text.IndexOf(quote, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return index;
        }

        // Quote was whitespace-normalized; fall back to the first few words.
        var firstWords = quote.Length > 40 ? quote[..40] : quote;
        index = text.IndexOf(firstWords, StringComparison.OrdinalIgnoreCase);
        return Math.Max(0, index);
    }
}
