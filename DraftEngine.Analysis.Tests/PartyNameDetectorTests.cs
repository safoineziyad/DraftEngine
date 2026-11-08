using DraftEngine.Analysis;

namespace DraftEngine.Analysis.Tests;

public sealed class PartyNameDetectorTests
{
    [Fact]
    public void FindsEntityNamesAndBetweenParties()
    {
        var names = PartyNameDetector.Detect(
            "This Agreement is entered into between Acme Holdings Inc. and Beta Labs LLC, effective today. " +
            "Acme Holdings Inc. shall protect information. Gamma Consulting SARL agrees.");

        Assert.Contains("Acme Holdings Inc.", names);
        Assert.Contains("Beta Labs LLC", names);
        Assert.Contains("Gamma Consulting SARL", names);
    }

    [Fact]
    public void Ignore_RegularSentences()
    {
        Assert.Empty(PartyNameDetector.Detect("Each party shall protect confidential information using reasonable care."));
    }
}
