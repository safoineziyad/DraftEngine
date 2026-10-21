namespace DraftEngine.Tests.Documents;

/// <summary>
/// Tests for the internal contract text parser used by both document builders
/// (reached through <see cref="DocumentInternals"/>). The parser splits assembled
/// contract text into numbered clause headings ("1. Confidentiality") and clause
/// paragraphs, skipping blank lines and trimming each line. Line endings are
/// normalised, so CRLF and LF input parse identically.
/// </summary>
public class ContractTextParserTests
{
    /// <summary>
    /// Numbered lines ("1. Confidentiality") become heading segments and their body lines
    /// become paragraph segments, in order.
    /// </summary>
    [Fact]
    public void Parse_NumberedClauses_ProduceHeadingAndParagraphSegments()
    {
        var segments = DocumentInternals.Parse(
            "1. Confidentiality\nEach party shall keep the Technical Information secret.\n2. Term\nThis Agreement lasts twelve (12) months.");

        Assert.Equal(4, segments.Count);
        Assert.True(segments[0].IsHeading);
        Assert.Equal("1. Confidentiality", segments[0].Text);
        Assert.False(segments[1].IsHeading);
        Assert.Equal("Each party shall keep the Technical Information secret.", segments[1].Text);
        Assert.True(segments[2].IsHeading);
        Assert.Equal("2. Term", segments[2].Text);
        Assert.False(segments[3].IsHeading);
        Assert.Equal("This Agreement lasts twelve (12) months.", segments[3].Text);
    }

    /// <summary>
    /// A four-clause document parses to eight segments alternating heading/paragraph.
    /// </summary>
    [Fact]
    public void Parse_FourClauseDocument_KeepsAlternatingOrderAndCount()
    {
        var segments = DocumentInternals.Parse("""
            1. Confidentiality
            Each party shall keep confidential all Technical Information disclosed by the other party.

            2. Term and Termination
            This Agreement continues for twelve (12) months unless terminated earlier.

            3. Return of Materials
            Upon termination the Receiving Party shall return all materials.

            4. Governing Law
            This Agreement is governed by the laws of England and Wales.
            """);

        Assert.Equal(8, segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            if (i % 2 == 0)
            {
                Assert.True(segments[i].IsHeading, $"Segment {i} should be a heading.");
            }
            else
            {
                Assert.False(segments[i].IsHeading, $"Segment {i} should be a paragraph.");
            }
        }
    }

    /// <summary>
    /// Headings may follow one another directly, without body paragraphs in between.
    /// </summary>
    [Fact]
    public void Parse_ConsecutiveHeadingsWithoutBody_AreAllHeadings()
    {
        var segments = DocumentInternals.Parse("1. Confidentiality\n2. Term\n3. Notices");

        Assert.Equal(3, segments.Count);
        Assert.All(segments, segment => Assert.True(segment.IsHeading));
    }

    /// <summary>
    /// "1.Confidentiality" (no space after the dot) and a bare "2." are not headings:
    /// the pattern requires whitespace followed by a non-whitespace character.
    /// </summary>
    [Fact]
    public void Parse_NumberedLineWithoutSpaceOrBareNumber_IsParagraph()
    {
        var segments = DocumentInternals.Parse("1.Confidentiality\nNo space after the dot.\n2.\nJust a number.");

        Assert.Equal(4, segments.Count);
        Assert.False(segments[0].IsHeading); // "1.Confidentiality" — the pattern requires whitespace after the dot.
        Assert.False(segments[2].IsHeading); // "2." — nothing follows the dot.
    }

    /// <summary>
    /// Any amount of whitespace after the number-dot still yields a heading.
    /// </summary>
    [Fact]
    public void Parse_MultipleSpacesAfterTheNumber_IsStillAHeading()
    {
        var segments = DocumentInternals.Parse("12.   Governance");

        var only = Assert.Single(segments);
        Assert.True(only.IsHeading);
        Assert.Equal("12.   Governance", only.Text);
    }

    /// <summary>
    /// A colon-terminated line of at most 80 characters is a heading; longer ones are paragraphs.
    /// </summary>
    [Fact]
    public void Parse_ColonTerminatedLines_UpToEightyCharacters_AreHeadings()
    {
        var atLimit = new string('a', 79) + ":"; // Exactly 80 characters.
        var overLimit = new string('a', 80) + ":"; // 81 characters.

        var segments = DocumentInternals.Parse($"Recitals:\nShort body.\n{atLimit}\nBody.\n{overLimit}\nBody two.");

        Assert.Equal(6, segments.Count);
        Assert.True(segments[0].IsHeading);
        Assert.True(segments[2].IsHeading);
        Assert.False(segments[4].IsHeading); // Over the 80-character limit → ordinary paragraph.
    }

    /// <summary>
    /// The parser performs no title/preamble splitting: lines before the first numbered
    /// heading are ordinary paragraphs (the builders take the title from Contract.Title).
    /// </summary>
    [Fact]
    public void Parse_TitleAndPreambleLines_BecomeOrdinaryParagraphs()
    {
        // The parser has no notion of a document title or preamble: the builders take the
        // title from Contract.Title and render everything before the first numbered heading
        // as ordinary paragraphs.
        var segments = DocumentInternals.Parse(
            "MASTER SERVICES AGREEMENT\nThis Agreement is made on 15 January 2026.\n1. Services\nThe Supplier shall provide the Services.");

        Assert.Equal(4, segments.Count);
        Assert.False(segments[0].IsHeading);
        Assert.Equal("MASTER SERVICES AGREEMENT", segments[0].Text);
        Assert.False(segments[1].IsHeading);
        Assert.True(segments[2].IsHeading);
        Assert.Equal("1. Services", segments[2].Text);
        Assert.False(segments[3].IsHeading);
    }

    /// <summary>
    /// A document without any recognised headings parses entirely into paragraphs.
    /// </summary>
    [Fact]
    public void Parse_DocumentWithoutAnyHeadings_AllLinesAreParagraphs()
    {
        var segments = DocumentInternals.Parse(
            "First line of an unstructured document.\nSecond line, also ordinary.\nThird line.");

        Assert.Equal(3, segments.Count);
        Assert.All(segments, segment => Assert.False(segment.IsHeading));
    }

    /// <summary>
    /// Blank lines and whitespace-only lines are skipped; every kept line is trimmed
    /// (outer whitespace only — inner spacing is preserved).
    /// </summary>
    [Fact]
    public void Parse_LeadingTrailingWhitespaceAndBlankLines_AreSkippedAndTrimmed()
    {
        var segments = DocumentInternals.Parse(
            "\r\n\r\n   1.   Confidentiality   \r\n\t\tEach party shall act reasonably.\t\t\r\n\r\n   \t \r\n   2. Notices\r\n   Done.   \r\n\r\n");

        Assert.Equal(4, segments.Count);
        Assert.True(segments[0].IsHeading);
        Assert.Equal("1.   Confidentiality", segments[0].Text); // Outer whitespace trimmed, inner spacing kept.
        Assert.Equal("Each party shall act reasonably.", segments[1].Text);
        Assert.True(segments[2].IsHeading);
        Assert.Equal("2. Notices", segments[2].Text);
        Assert.Equal("Done.", segments[3].Text);
        Assert.All(segments, segment => Assert.False(string.IsNullOrWhiteSpace(segment.Text)));
    }

    /// <summary>
    /// CRLF, LF and lone-CR input for the same logical document produce identical segments.
    /// </summary>
    [Fact]
    public void Parse_CrlfLfAndCrLineEndings_ProduceIdenticalSegments()
    {
        string[] lines =
        {
            "1. Confidentiality",
            "Each party shall keep the Technical Information secret.",
            "2. Term",
            "This Agreement lasts twelve (12) months."
        };

        var lf = DocumentInternals.Parse(string.Join("\n", lines));
        var crlf = DocumentInternals.Parse(string.Join("\r\n", lines));
        var cr = DocumentInternals.Parse(string.Join("\r", lines));

        Assert.Equal(4, lf.Count);
        AssertSameSegments(lf, crlf);
        AssertSameSegments(lf, cr);
    }

    /// <summary>
    /// A single document mixing CRLF, CR and LF line endings still parses correctly.
    /// </summary>
    [Fact]
    public void Parse_MixedLineEndings_InOneDocument_AreHandled()
    {
        var segments = DocumentInternals.Parse("1. Confidentiality\r\nBody A.\rBody B.\n2. Term\r\nBody C.");

        Assert.Equal(5, segments.Count);
        Assert.True(segments[0].IsHeading);
        Assert.Equal("Body A.", segments[1].Text);
        Assert.Equal("Body B.", segments[2].Text);
        Assert.True(segments[3].IsHeading);
        Assert.Equal("Body C.", segments[4].Text);
    }

    /// <summary>
    /// Null, empty and whitespace-only input produce an empty segment list.
    /// </summary>
    [Fact]
    public void Parse_NullEmptyOrWhitespaceInput_ReturnsNoSegments()
    {
        Assert.Empty(DocumentInternals.Parse(null));
        Assert.Empty(DocumentInternals.Parse(string.Empty));
        Assert.Empty(DocumentInternals.Parse("   "));
        Assert.Empty(DocumentInternals.Parse("\r\n\t  \r\n"));
    }

    private static void AssertSameSegments(
        IReadOnlyList<DocumentInternals.Segment> expected,
        IReadOnlyList<DocumentInternals.Segment> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal((expected[i].IsHeading, expected[i].Text), (actual[i].IsHeading, actual[i].Text));
        }
    }
}
