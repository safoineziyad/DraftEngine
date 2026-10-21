using System.Reflection;

namespace DraftEngine.App;

/// <summary>
/// Exposes the application version to the UI. The single source of truth is the
/// <c>&lt;Version&gt;</c> element in DraftEngine.App.csproj: the compiler stamps it into the
/// assembly, and XAML labels bind to <see cref="Display"/> so the window can never drift
/// from the real release version (audit Finding #9).
/// </summary>
public static class AppVersion
{
    /// <summary>The three-part version string shown in the UI, e.g. <c>"0.2.0"</c>.</summary>
    public static string Display { get; } = ResolveDisplayVersion();

    private static string ResolveDisplayVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : version.ToString(3);
    }
}
