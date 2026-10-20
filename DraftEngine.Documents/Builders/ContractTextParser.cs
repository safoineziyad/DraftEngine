using System.Text.RegularExpressions;

namespace DraftEngine.Documents.Builders;

/// <summary>
/// Kind of segment produced when parsing assembled contract text.
/// </summary>
internal enum ContractSegmentKind
{
    Heading,
    Paragraph
}

/// <summary>
/// A single parsed piece of contract text (a clause heading or a clause paragraph).
/// </summary>
internal sealed class ContractSegment
{
    internal ContractSegment(ContractSegmentKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    internal ContractSegmentKind Kind { get; }

    internal string Text { get; }

    internal bool IsHeading => Kind == ContractSegmentKind.Heading;
}

/// <summary>
/// Parses assembled contract text, which is numbered clause headings followed by clause paragraphs
/// (e.g. "1. Confidentiality" then the clause body).
/// </summary>
internal static class ContractTextParser
{
    private static readonly Regex NumberedHeadingPattern = new(@"^\d+\.\s+\S", RegexOptions.Compiled);

    private const int ColonHeadingMaxLength = 80;

    /// <summary>
    /// Splits the contract text into heading/paragraph segments, skipping blank lines.
    /// </summary>
    internal static IReadOnlyList<ContractSegment> Parse(string fullContractText)
    {
        var segments = new List<ContractSegment>();
        if (string.IsNullOrWhiteSpace(fullContractText))
        {
            return segments;
        }

        var normalized = fullContractText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var kind = IsHeading(line) ? ContractSegmentKind.Heading : ContractSegmentKind.Paragraph;
            segments.Add(new ContractSegment(kind, line));
        }

        return segments;
    }

    private static bool IsHeading(string line) =>
        NumberedHeadingPattern.IsMatch(line) || IsColonHeading(line);

    private static bool IsColonHeading(string line) =>
        line.Length <= ColonHeadingMaxLength && line.EndsWith(':');
}
