using System.Text.RegularExpressions;

namespace DraftEngine.Analysis;

public enum ClauseChangeKind
{
    Unchanged,
    Added,
    Removed,
    Modified
}

public sealed record ClauseChange(
    ClauseChangeKind Kind,
    string Heading,
    string? OlderText,
    string? NewerText,
    double Similarity);

/// <summary>Clause-level "what changed" summary between two versions or two documents.</summary>
public sealed class DocumentComparison
{
    public required IReadOnlyList<ClauseChange> Changes { get; init; }

    public int AddedCount => Changes.Count(change => change.Kind == ClauseChangeKind.Added);
    public int RemovedCount => Changes.Count(change => change.Kind == ClauseChangeKind.Removed);
    public int ModifiedCount => Changes.Count(change => change.Kind == ClauseChangeKind.Modified);
    public int UnchangedCount => Changes.Count(change => change.Kind == ClauseChangeKind.Unchanged);

    /// <summary>Compares clause headings first, then text similarity for the same heading.</summary>
    public static DocumentComparison Compare(NormalizedDocument older, NormalizedDocument newer)
    {
        var oldClauses = ClauseSegmenter.Segment(older)
            .Where(clause => clause.Category != "Preamble")
            .ToList();
        var newClauses = ClauseSegmenter.Segment(newer)
            .Where(clause => clause.Category != "Preamble")
            .ToList();

        var changes = new List<ClauseChange>();
        var matchedNew = new HashSet<int>();

        foreach (var oldClause in oldClauses)
        {
            var key = NormalizeHeading(oldClause.Heading);
            var matchIndex = newClauses.FindIndex(candidate =>
                !matchedNew.Contains(candidate.Number) &&
                NormalizeHeading(candidate.Heading) == key);

            if (matchIndex < 0)
            {
                changes.Add(new ClauseChange(ClauseChangeKind.Removed, oldClause.Heading, oldClause.Text, null, 0));
                continue;
            }

            matchedNew.Add(newClauses[matchIndex].Number);
            var newerClause = newClauses[matchIndex];
            var similarity = Similarity(oldClause.Text, newerClause.Text);
            changes.Add(new ClauseChange(
                similarity >= 0.92 ? ClauseChangeKind.Unchanged : ClauseChangeKind.Modified,
                oldClause.Heading,
                oldClause.Text,
                newerClause.Text,
                similarity));
        }

        foreach (var newClause in newClauses.Where(clause => !matchedNew.Contains(clause.Number)))
        {
            changes.Add(new ClauseChange(ClauseChangeKind.Added, newClause.Heading, null, newClause.Text, 1));
        }

        return new DocumentComparison { Changes = changes };
    }

    private static string NormalizeHeading(string heading)
    {
        var withoutNumber = Regex.Replace(heading, @"^\s*(?:\d+(?:\.\d+)*|[IVXLC]+)[.)]?\s*", string.Empty,
            RegexOptions.IgnoreCase);
        return Regex.Replace(withoutNumber.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
    }

    private static double Similarity(string left, string right)
    {
        var leftTokens = Tokens(left);
        var rightTokens = Tokens(right);
        if (leftTokens.Count == 0 && rightTokens.Count == 0)
        {
            return 1;
        }

        var intersection = leftTokens.Intersect(rightTokens).Count();
        var union = leftTokens.Union(rightTokens).Count();
        return union == 0 ? 1 : (double)intersection / union;
    }

    private static HashSet<string> Tokens(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]+")
            .Select(match => match.Value)
            .Where(token => token.Length > 2)
            .ToHashSet(StringComparer.Ordinal);
}
