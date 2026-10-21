using DraftEngine.Core.Services;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for the <c>{{Placeholder}}</c> pattern handling in <see cref="FieldService"/>
/// (audit Findings #17/#11): hyphenated and dotted keys must substitute like word keys,
/// unknown keys must stay literal so nothing silently disappears from the document.
/// </summary>
public class FieldServicePlaceholderTests
{
    private readonly FieldService _fieldService = new();

    [Fact]
    public void ReplacePlaceholders_HyphenatedKey_IsSubstituted()
    {
        var resolved = _fieldService.ReplacePlaceholders(
            "Signed by {{Party-1}} and {{Party-2}}.",
            new Dictionary<string, string>
            {
                ["Party-1"] = "Acme Inc",
                ["Party-2"] = "Globex Ltd",
            });

        Assert.Equal("Signed by Acme Inc and Globex Ltd.", resolved);
    }

    [Fact]
    public void ReplacePlaceholders_DottedKey_IsSubstituted()
    {
        var resolved = _fieldService.ReplacePlaceholders(
            "Notice: {{client.email}}.",
            new Dictionary<string, string>
            {
                ["client.email"] = "legal@acme.example",
            });

        Assert.Equal("Notice: legal@acme.example.", resolved);
    }

    [Fact]
    public void ReplacePlaceholders_UnderscoreKey_IsSubstituted()
    {
        var resolved = _fieldService.ReplacePlaceholders(
            "Term: {{Term_Months}} months.",
            new Dictionary<string, string> { ["Term_Months"] = "24" });

        Assert.Equal("Term: 24 months.", resolved);
    }

    [Fact]
    public void ReplacePlaceholders_UnknownKey_StaysLiteral()
    {
        var resolved = _fieldService.ReplacePlaceholders(
            "Value: {{Nonexistent-Key.v2}}.",
            new Dictionary<string, string>());

        // Unresolved placeholders remain visible (and are logged) rather than vanishing.
        Assert.Equal("Value: {{Nonexistent-Key.v2}}.", resolved);
    }

    [Fact]
    public void ReplacePlaceholders_SupportedKeyWithoutValue_FallsBackToDefault()
    {
        var resolved = _fieldService.ReplacePlaceholders(
            "Term: {{TermMonths}} months.",
            new Dictionary<string, string>());

        Assert.Equal("Term: 12 months.", resolved);
    }
}
