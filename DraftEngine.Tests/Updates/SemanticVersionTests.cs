using DraftEngine.Updates;

namespace DraftEngine.Tests.Updates;

/// <summary>SemVer 2.0.0 parsing, ordering and prerelease precedence.</summary>
public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("0.4.0-beta.1", "0.4.0-beta.2")]
    [InlineData("0.4.0-beta.2", "0.4.0-beta.10")]
    [InlineData("0.4.0-beta.10", "0.4.0")]
    [InlineData("0.4.0", "0.4.1")]
    [InlineData("0.4.1", "0.5.0")]
    [InlineData("0.5.0", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("v1.2.3", "1.2.4")]
    public void CandidatesAreNewer(string current, string candidate)
    {
        Assert.True(SemanticVersion.IsNewer(candidate, current));
        Assert.False(SemanticVersion.IsNewer(current, candidate));
    }

    [Theory]
    [InlineData("1.0.0+build.1", "1.0.0+build.2")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("v0.4.0", "0.4.0")]
    public void BuildMetadataAndVersionPrefixDoNotAffectOrdering(string left, string right)
    {
        Assert.True(SemanticVersion.TryParse(left, out var a));
        Assert.True(SemanticVersion.TryParse(right, out var b));
        Assert.Equal(0, a.CompareTo(b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3+")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.3 beta")]
    public void InvalidVersionsAreRejected(string value)
    {
        Assert.False(SemanticVersion.TryParse(value, out _));
    }

    [Fact]
    public void PrereleaseIsReportedAndToStringDropsBuildMetadata()
    {
        var version = SemanticVersion.Parse("0.4.0-beta.2+abcdef");

        Assert.True(version.IsPrerelease);
        Assert.Equal("beta.2", version.Prerelease);
        Assert.Equal("0.4.0-beta.2", version.ToString());
        Assert.Equal("abcdef", version.BuildMetadata);
    }
}
