using System.Reflection;
using System;
using System.Linq;

namespace DraftEngine.App;

/// <summary>
/// Exposes the application version and build metadata stamped into the assembly.
/// </summary>
public static class AppVersion
{
    /// <summary>The release version shown in the UI, including prerelease suffixes.</summary>
    public static string Display { get; } = ResolveDisplayVersion();

    /// <summary>Commit and UTC build date supplied by the installer build script.</summary>
    public static string BuildStamp { get; } =
        $"Commit {GetMetadata("BuildCommitHash")} · {GetMetadata("BuildDateUtc")}";

    private static string ResolveDisplayVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', 2)[0];
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    private static string GetMetadata(string key) =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value ?? "unknown";
}
