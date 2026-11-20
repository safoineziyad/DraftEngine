using System.IO;

namespace DraftEngine.App.Tests;

/// <summary>
/// Simple checks that the shipped configuration exposes the About/Help page and that the page
/// itself presents the version, build stamp (commit + build date) and data-folder link.
/// No WPF rendering or visual-tree traversal is involved.
/// </summary>
public sealed class AboutNavigationTests
{
    [Fact]
    public void ShippedConfig_EnablesAboutInSidebar()
    {
        var appDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "DraftEngine.App"));
        var config = File.ReadAllText(Path.Combine(appDir, "appsettings.json"));

        Assert.Contains("\"AboutEnabled\": true", config);
    }

    [Fact]
    public void AboutView_ShowsVersionBuildStampAndDataFolderLink()
    {
        var viewsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "DraftEngine.App", "Views"));
        var xaml = File.ReadAllText(Path.Combine(viewsDir, "AboutView.xaml"));

        Assert.Contains("Text=\"{Binding VersionText, StringFormat=Version {0}}\"", xaml);
        Assert.Contains("Text=\"{Binding BuildStampText}\"", xaml);
        Assert.Contains("Command=\"{Binding OpenDataFolderCommand}\"", xaml);
    }
}
