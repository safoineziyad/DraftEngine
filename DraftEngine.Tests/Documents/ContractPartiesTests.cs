namespace DraftEngine.Tests.Documents;

/// <summary>
/// Tests for the internal parties helper used by both document builders (reached through
/// <see cref="DocumentInternals"/>): extracting party names from a contract's PartiesJson
/// (array of objects with a case-insensitive "name" property, or an array of strings)
/// and producing the human-readable parties display text.
/// </summary>
public class ContractPartiesTests
{
    private const string TwoObjectParties =
        """[{"name":"Acme Corp","role":"Disclosing Party"},{"name":"Globex Ltd","role":"Receiving Party"}]""";

    /// <summary>
    /// A JSON array of two objects with a "name" property yields both names, in order.
    /// </summary>
    [Fact]
    public void GetNames_TwoObjectParties_ReturnsBothNamesInOrder()
    {
        var names = DocumentInternals.GetPartyNames(TwoObjectParties);

        Assert.Equal(new[] { "Acme Corp", "Globex Ltd" }, names);
    }

    /// <summary>
    /// A JSON array with a single object party yields that one name.
    /// </summary>
    [Fact]
    public void GetNames_SingleObjectParty_ReturnsThatName()
    {
        var names = DocumentInternals.GetPartyNames("""[{"name":"Acme Corp","role":"Disclosing Party"}]""");

        Assert.Equal(new[] { "Acme Corp" }, names);
    }

    /// <summary>
    /// A plain JSON array of strings is also supported and yields all names.
    /// </summary>
    [Fact]
    public void GetNames_StringArray_ReturnsAllNames()
    {
        var names = DocumentInternals.GetPartyNames("""["Acme Corp","Globex Ltd"]""");

        Assert.Equal(new[] { "Acme Corp", "Globex Ltd" }, names);
    }

    /// <summary>
    /// The parser is not limited to two entries: three or more parties are all extracted.
    /// </summary>
    [Fact]
    public void GetNames_ThreeOrMoreEntries_AreAllSupported()
    {
        var names = DocumentInternals.GetPartyNames(
            """[{"name":"Acme Corp"},{"name":"Globex Ltd"},{"name":"Initech LLC"},{"name":"Umbrella Corp"}]""");

        Assert.Equal(new[] { "Acme Corp", "Globex Ltd", "Initech LLC", "Umbrella Corp" }, names);
    }

    /// <summary>
    /// The "name" property match is case-insensitive and the value is trimmed.
    /// </summary>
    [Fact]
    public void GetNames_NamePropertyMatch_IsCaseInsensitive_AndTrimmed()
    {
        var names = DocumentInternals.GetPartyNames("""[{"Name":"  Acme Corp  "}]""");

        Assert.Equal(new[] { "Acme Corp" }, names);
    }

    /// <summary>
    /// Missing "address"/"role" fields do not affect name extraction, and an object without
    /// a "name" property is skipped entirely.
    /// </summary>
    [Fact]
    public void GetNames_MissingAddressOrRoleFields_DoesNotAffectNameExtraction()
    {
        var names = DocumentInternals.GetPartyNames(
            """[{"name":"Acme Corp"},{"address":"1 Main Street"},{"name":"Globex Ltd","address":"2 Side Road"}]""");

        Assert.Equal(new[] { "Acme Corp", "Globex Ltd" }, names);
    }

    /// <summary>
    /// Empty or whitespace-only name values are ignored.
    /// </summary>
    [Fact]
    public void GetNames_BlankNames_AreIgnored()
    {
        var names = DocumentInternals.GetPartyNames("""[{"name":""},{"name":"   "},{"name":"Acme Corp"}]""");

        Assert.Equal(new[] { "Acme Corp" }, names);
    }

    /// <summary>
    /// Invalid or truncated JSON yields no names (the raw-text fallback for invalid JSON
    /// lives in ToDisplayText, see <see cref="ToDisplayText_InvalidJson_FallsBackToTrimmedRawText"/>).
    /// </summary>
    [Fact]
    public void GetNames_TruncatedJson_ReturnsNoNames_FallbackIsInToDisplayText()
    {
        Assert.Empty(DocumentInternals.GetPartyNames("""[{"name":"Acme Corp"""));
        Assert.Empty(DocumentInternals.GetPartyNames("not json at all"));
    }

    /// <summary>
    /// A valid JSON object (instead of an array) yields no names — only arrays are supported.
    /// </summary>
    [Fact]
    public void GetNames_JsonObjectInsteadOfArray_ReturnsNoNames()
    {
        Assert.Empty(DocumentInternals.GetPartyNames("""{"name":"Acme Corp"}"""));
    }

    /// <summary>
    /// Null, empty and whitespace-only input yield no names.
    /// </summary>
    [Fact]
    public void GetNames_NullEmptyOrWhitespace_ReturnsNoNames()
    {
        Assert.Empty(DocumentInternals.GetPartyNames(null));
        Assert.Empty(DocumentInternals.GetPartyNames(string.Empty));
        Assert.Empty(DocumentInternals.GetPartyNames("   "));
    }

    /// <summary>
    /// Two parties are joined with " and " for the title-page display text.
    /// </summary>
    [Fact]
    public void ToDisplayText_TwoParties_AreJoinedWithAnd()
    {
        var text = DocumentInternals.GetPartiesDisplayText(TwoObjectParties);

        Assert.Equal("Acme Corp and Globex Ltd", text);
    }

    /// <summary>
    /// Three parties are joined with " and " as well (no special Oxford-comma handling).
    /// </summary>
    [Fact]
    public void ToDisplayText_ThreeParties_AreJoinedWithAnd()
    {
        var text = DocumentInternals.GetPartiesDisplayText("""["Acme Corp","Globex Ltd","Initech LLC"]""");

        Assert.Equal("Acme Corp and Globex Ltd and Initech LLC", text);
    }

    /// <summary>
    /// When PartiesJson is invalid/truncated, the display text falls back to the trimmed raw
    /// text — the Documents-side equivalent of the fallback in ContractService.AddPartyValues
    /// (DraftEngine.Core/Services/ContractService.cs).
    /// </summary>
    [Fact]
    public void ToDisplayText_InvalidJson_FallsBackToTrimmedRawText()
    {
        var text = DocumentInternals.GetPartiesDisplayText("  [this is not valid JSON");

        Assert.Equal("[this is not valid JSON", text);
    }

    /// <summary>
    /// Valid JSON that is not an array also falls back to the raw text as display text.
    /// </summary>
    [Fact]
    public void ToDisplayText_NonArrayJson_FallsBackToRawText()
    {
        const string raw = """{"name":"Acme Corp"}""";

        var text = DocumentInternals.GetPartiesDisplayText(raw);

        Assert.Equal(raw, text);
    }

    /// <summary>
    /// Null, empty and whitespace-only input display as "N/A".
    /// </summary>
    [Fact]
    public void ToDisplayText_NullOrWhitespace_IsNotApplicable()
    {
        Assert.Equal("N/A", DocumentInternals.GetPartiesDisplayText(null));
        Assert.Equal("N/A", DocumentInternals.GetPartiesDisplayText(string.Empty));
        Assert.Equal("N/A", DocumentInternals.GetPartiesDisplayText("   "));
    }
}
