using System;

namespace DraftEngine.Core.Updates;

/// <summary>
/// Compares release versions from an update manifest with the running application version.
/// Handles an optional leading <c>v</c> and prerelease/build suffixes (e.g. <c>1.2.3-beta</c>).
/// </summary>
public static class UpdateVersionComparer
{
    /// <summary>Returns true only when <paramref name="candidate"/> is strictly newer.</summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        if (!TryParse(candidate, out var candidateVersion) ||
            !TryParse(current, out var currentVersion))
        {
            return false;
        }

        return candidateVersion > currentVersion;
    }

    private static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        // Drop prerelease/build metadata: the comparison is intentionally simple and only
        // answers "is there a newer release to tell the user about?".
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        return Version.TryParse(normalized, out version!);
    }
}
