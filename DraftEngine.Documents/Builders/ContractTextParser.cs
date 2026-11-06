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

        var normalized = Normalize(fullContractText);
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

    /// <summary>
    /// Parses only the clause body of assembled contract text: everything from the first
    /// "IN WITNESS WHEREOF" line onward is dropped because the DOCX/PDF builders render
    /// their own signature block from the contract type's role labels. Without this, the
    /// assembled signature paragraph was rendered twice in every exported document.
    /// </summary>
    internal static IReadOnlyList<ContractSegment> ParseBody(string fullContractText)
    {
        if (string.IsNullOrWhiteSpace(fullContractText))
        {
            return new List<ContractSegment>();
        }

        var normalized = Normalize(fullContractText);
        var end = normalized.Length;
        foreach (var rawLine in normalized.Split('\n'))
        {
            if (rawLine.TrimStart().StartsWith("IN WITNESS WHEREOF", StringComparison.OrdinalIgnoreCase))
            {
                end = normalized.IndexOf(rawLine, StringComparison.Ordinal);
                break;
            }
        }

        return Parse(normalized[..end]);
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static bool IsHeading(string line) =>
        NumberedHeadingPattern.IsMatch(line) || IsColonHeading(line);

    private static bool IsColonHeading(string line) =>
        line.Length <= ColonHeadingMaxLength && line.EndsWith(':');
}
