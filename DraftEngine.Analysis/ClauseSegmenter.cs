using System.Text.RegularExpressions;

namespace DraftEngine.Analysis;

/// <summary>
/// Splits a normalized document into clauses at numbered or styled headings and assigns each
/// clause a category from its heading/first lines. Heuristics only: no AI, no network.
/// </summary>
public static class ClauseSegmenter
{
    private static readonly (string Category, string[] Keywords)[] Categories =
    [
        ("Confidentiality", ["confidential", "non-disclosure", "nondisclosure", "trade secret"]),
        ("Definitions", ["definition", "defined terms", "interpretation"]),
        ("Exclusions", ["exclusion", "carve-out", "carve out", "exception"]),
        ("Term", ["term", "duration", "expiration", "termination"]),
        ("Governing Law", ["governing law", "choice of law", "jurisdiction", "venue"]),
        ("Return of Materials", ["return of materials", "return or destroy", "return all", "destroy all"]),
        ("Remedies", ["remedies", "injunctive relief", "equitable relief", "specific performance"]),
        ("Intellectual Property", ["intellectual property", "work product", "ownership", "copyright", "patent"]),
        ("Compensation", ["compensation", "payment", "fees", "invoice", "expense"]),
        ("Indemnification", ["indemnif", "hold harmless", "defend"]),
        ("Liability", ["limitation of liability", "liability", "consequential damages"]),
        ("Non-Solicitation", ["non-solicit", "non solicit", "solicit", "non-compete"]),
        ("Assignment", ["assignment", "assign"]),
        ("Obligations", ["obligation", "duties", "shall protect", "receiving party"]),
        ("Services", ["services", "scope of work", "deliverable", "statement of work"]),
        ("Engagement", ["engagement", "services", "contractor", "consultant"]),
        ("Status", ["independent contractor", "status", "employment"]),
        ("Miscellaneous", ["miscellaneous", "general provisions", "entire agreement", "severability", "notices"])
    ];

    private static readonly Regex ClauseNumber = new(
        @"^\s*(?:(\d+(?:\.\d+)*)|([IVXLC]+))\s*[.)]?\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Segments the document into clauses (including a preamble when present).</summary>
    public static IReadOnlyList<AnalyzedClause> Segment(NormalizedDocument document)
    {
        if (document.Paragraphs.Count == 0)
        {
            return [];
        }

        var clauses = new List<AnalyzedClause>();
        var numberedHeadings = document.Paragraphs
            .Select((paragraph, index) => (paragraph, index))
            .Where(item => ClauseNumber.IsMatch(item.paragraph.Text))
            .Select(item => item.index)
            .ToList();

        if (numberedHeadings.Count == 0)
        {
            return [BuildClause(document, 1, 1, "Contract", "Contract",
                document.Paragraphs[0].StartOffset,
                document.Paragraphs[^1].EndOffset)];
        }

        // Everything before the first numbered clause (title, parties, recitals) is preamble:
        // it must not satisfy a required-clause keyword search.
        var firstHeading = numberedHeadings[0];
        if (firstHeading > 0)
        {
            clauses.Add(BuildClause(document, 0, 0, "Preamble", "Preamble",
                document.Paragraphs[0].StartOffset,
                document.Paragraphs[firstHeading - 1].EndOffset));
        }

        var headingIndexes = document.Paragraphs
            .Select((paragraph, index) => (paragraph, index))
            .Where(item => item.index >= firstHeading && item.paragraph.IsHeading)
            .Select(item => item.index)
            .ToList();

        for (var position = 0; position < headingIndexes.Count; position++)
        {
            var startIndex = headingIndexes[position];
            var endIndex = position + 1 < headingIndexes.Count
                ? headingIndexes[position + 1] - 1
                : document.Paragraphs.Count - 1;

            var heading = document.Paragraphs[startIndex].Text.Trim();
            var body = string.Join(
                "\n",
                document.Paragraphs.Skip(startIndex + 1).Take(endIndex - startIndex).Select(paragraph => paragraph.Text));
            var category = InferCategory(heading, body);
            var number = ParseNumber(heading) ?? position + 1;

            clauses.Add(BuildClause(
                document,
                position + 1,
                number,
                heading,
                category,
                document.Paragraphs[startIndex].StartOffset,
                document.Paragraphs[endIndex].EndOffset));
        }

        return clauses;
    }

    /// <summary>Infers a clause category from a heading and its body.</summary>
    public static string InferCategory(string heading, string body)
    {
        var headingText = heading.ToLowerInvariant();

        // The heading is the strongest signal: "Return of Materials" must not become
        // "Confidentiality" just because its body mentions confidential materials.
        foreach (var (category, keywords) in Categories)
        {
            if (keywords.Any(keyword => headingText.Contains(keyword, StringComparison.Ordinal)))
            {
                return category;
            }
        }

        var bodyText = Truncate(body, 400).ToLowerInvariant();
        foreach (var (category, keywords) in Categories)
        {
            if (keywords.Any(keyword => bodyText.Contains(keyword, StringComparison.Ordinal)))
            {
                return category;
            }
        }

        return "Other";
    }

    private static AnalyzedClause BuildClause(
        NormalizedDocument document,
        int id,
        int number,
        string heading,
        string category,
        int startOffset,
        int endOffset)
    {
        var text = document.Text[startOffset..Math.Min(endOffset, document.Text.Length)];
        return new AnalyzedClause
        {
            Id = id == 0 ? "preamble" : $"clause-{id}",
            Number = number,
            Heading = heading,
            Category = category,
            Text = text,
            StartOffset = startOffset,
            EndOffset = endOffset
        };
    }

    private static int? ParseNumber(string heading)
    {
        var match = ClauseNumber.Match(heading);
        if (match.Success && int.TryParse(match.Groups[1].Value.Split('.')[0], out var number))
        {
            return number;
        }

        return null;
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
