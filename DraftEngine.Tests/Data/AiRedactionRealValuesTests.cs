using DraftEngine.Core.Services;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests that real party identities (not just template placeholders) are removed from text
/// before an AI request and restored afterwards.
/// </summary>
public class AiRedactionRealValuesTests
{
    [Fact]
    public void Redact_ReplacesConfiguredValuesAndRestoresThem()
    {
        var service = new AiRedactionService { IsEnabled = true };
        service.Configure(["Acme Holdings Inc.", "1 Main Street", "jane@acme.test"]);
        var original =
            "Acme Holdings Inc. at 1 Main Street (jane@acme.test) agrees with Beta LLC.";

        var redacted = service.Redact(original);

        Assert.DoesNotContain("Acme Holdings Inc.", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("1 Main Street", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("jane@acme.test", redacted, StringComparison.Ordinal);
        Assert.Contains("Beta LLC", redacted, StringComparison.Ordinal);

        Assert.Equal(original, service.Restore(redacted));
    }

    [Fact]
    public void Redact_ReplacesLongerValuesBeforeShorterOnes()
    {
        var service = new AiRedactionService { IsEnabled = true };
        service.Configure(["Acme", "Acme Holdings Inc."]);

        var redacted = service.Redact("Acme Holdings Inc. and Acme");

        Assert.DoesNotContain("Acme Holdings Inc.", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme", redacted, StringComparison.Ordinal);
        Assert.Equal("Acme Holdings Inc. and Acme", service.Restore(redacted));
    }

    [Fact]
    public void Configure_ResetsTheTokenMapBetweenRequests()
    {
        var service = new AiRedactionService { IsEnabled = true };
        service.Configure(["First Party"]);
        Assert.NotEqual("First Party", service.Redact("First Party"));

        // Re-configuring must drop the previous values, not keep redacting them.
        service.Configure(["Second Party"]);
        Assert.Equal("First Party", service.Redact("First Party"));
        Assert.NotEqual("Second Party", service.Redact("Second Party"));
    }

    [Fact]
    public void DisabledRedaction_ReturnsConfiguredValuesUnchanged()
    {
        var service = new AiRedactionService { IsEnabled = false };
        service.Configure(["Acme Inc."]);

        Assert.Equal("Acme Inc.", service.Redact("Acme Inc."));
    }
}
