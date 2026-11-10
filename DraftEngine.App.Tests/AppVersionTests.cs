namespace DraftEngine.App.Tests;

public sealed class AppVersionTests
{
    [Fact]
    public void Display_UsesAssemblyInformationalVersionIncludingPrerelease()
    {
        Assert.Equal("0.4.0-beta.1", AppVersion.Display);
        Assert.Equal(new Version(0, 4, 0, 1), typeof(App).Assembly.GetName().Version);
    }

    [Fact]
    public void BuildStamp_ContainsCommitAndBuildDateMetadata()
    {
        Assert.StartsWith("Commit ", AppVersion.BuildStamp);
        Assert.Contains(" · ", AppVersion.BuildStamp);
    }
}
