using DraftEngine.Core.Services;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for <see cref="AiRedactionService" />: redaction/restore behavior with
/// <see cref="AiRedactionService.IsEnabled" /> on and off.
/// </summary>
public class AiRedactionServiceTests
{
    [Fact]
    public void Redact_RestoresRoundtrip_WhenEnabled()
    {
        var service = new AiRedactionService { IsEnabled = true };
        var original = "Hello {{Party1}} and {{Party2}}, from {{ClientName}}.";

        var redacted = service.Redact(original);
        var restored = service.Restore(redacted);

        Assert.DoesNotContain("{{Party1}}", redacted);
        Assert.DoesNotContain("{{Party2}}", redacted);
        Assert.DoesNotContain("{{ClientName}}", redacted);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Redact_ReturnsTextUnchanged_WhenDisabled()
    {
        var service = new AiRedactionService { IsEnabled = false };
        var text = "Hello {{Party1}}.";

        Assert.Equal(text, service.Redact(text));
        Assert.Equal(text, service.Restore(text));
    }

    [Fact]
    public void Redact_PreservesNonNamePlaceholders_WhenEnabled()
    {
        var service = new AiRedactionService { IsEnabled = true };
        var original = "Term is {{TermMonths}} months starting {{EffectiveDate}}.";

        var redacted = service.Redact(original);
        var restored = service.Restore(redacted);

        Assert.Equal(original, redacted);
        Assert.Equal(original, restored);
    }
}
