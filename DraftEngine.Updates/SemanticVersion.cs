using System.Globalization;

namespace DraftEngine.Updates;

/// <summary>
/// SemVer 2.0.0 version with prerelease-aware ordering:
/// <c>0.4.0-beta.1 &lt; 0.4.0-beta.2 &lt; 0.4.0-beta.10 &lt; 0.4.0 &lt; 0.4.1</c>.
/// Build metadata (<c>+sha</c>) is accepted and ignored for comparison.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private readonly string[] _prerelease;

    private SemanticVersion(int major, int minor, int patch, string[] prerelease, string? build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _prerelease = prerelease;
        BuildMetadata = build;
    }

    /// <summary>Major version number.</summary>
    public int Major { get; }

    /// <summary>Minor version number.</summary>
    public int Minor { get; }

    /// <summary>Patch version number.</summary>
    public int Patch { get; }

    /// <summary>Build metadata without the leading '+', or null.</summary>
    public string? BuildMetadata { get; }

    /// <summary>True when the version carries a prerelease tag.</summary>
    public bool IsPrerelease => _prerelease.Length > 0;

    /// <summary>The prerelease tag (without leading '-'), or an empty string.</summary>
    public string Prerelease => string.Join('.', _prerelease);

    /// <summary>Parses a SemVer string; a single leading <c>v</c> is tolerated.</summary>
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.Length > 1 && (text[0] == 'v' || text[0] == 'V') && char.IsDigit(text[1]))
        {
            text = text[1..];
        }

        string? build = null;
        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            build = text[(plus + 1)..];
            text = text[..plus];
            if (!IsValidIdentifierList(build, allowLeadingZeroNumbers: true))
            {
                return false;
            }
        }

        string[] prerelease = Array.Empty<string>();
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            var tag = text[(dash + 1)..];
            text = text[..dash];
            if (!IsValidIdentifierList(tag, allowLeadingZeroNumbers: false))
            {
                return false;
            }

            prerelease = tag.Split('.');
        }

        var parts = text.Split('.');
        if (parts.Length != 3 ||
            !TryParseNumber(parts[0], out var major) ||
            !TryParseNumber(parts[1], out var minor) ||
            !TryParseNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease, build);
        return true;
    }

    /// <summary>Parses or throws <see cref="FormatException"/>.</summary>
    public static SemanticVersion Parse(string value) =>
        TryParse(value, out var version) ? version : throw new FormatException($"'{value}' is not a semantic version.");

    /// <inheritdoc />
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // A release outranks any prerelease of the same core version.
        if (!IsPrerelease && !other.IsPrerelease) return 0;
        if (!IsPrerelease) return 1;
        if (!other.IsPrerelease) return -1;

        var length = Math.Min(_prerelease.Length, other._prerelease.Length);
        for (var i = 0; i < length; i++)
        {
            var left = _prerelease[i];
            var right = other._prerelease[i];
            var leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

            if (leftNumeric && rightNumeric)
            {
                result = leftNumber.CompareTo(rightNumber);
            }
            else if (leftNumeric)
            {
                result = -1; // numeric identifiers sort below alphanumeric ones
            }
            else if (rightNumeric)
            {
                result = 1;
            }
            else
            {
                result = string.CompareOrdinal(left, right);
            }

            if (result != 0) return result;
        }

        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    /// <summary>True when this version is strictly newer than <paramref name="other"/>.</summary>
    public bool IsNewerThan(SemanticVersion other) => CompareTo(other) > 0;

    /// <inheritdoc />
    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    /// <summary>Returns the normalized version without build metadata.</summary>
    public override string ToString() =>
        IsPrerelease
            ? $"{Major}.{Minor}.{Patch}-{Prerelease}"
            : $"{Major}.{Minor}.{Patch}";

    /// <summary>True when <paramref name="candidate"/> is newer than <paramref name="current"/>; false if either is invalid.</summary>
    public static bool IsNewer(string? candidate, string? current) =>
        TryParse(candidate, out var candidateVersion) &&
        TryParse(current, out var currentVersion) &&
        candidateVersion.IsNewerThan(currentVersion);

    private static bool TryParseNumber(string text, out int number) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 0;

    private static bool IsValidIdentifierList(string value, bool allowLeadingZeroNumbers)
    {
        if (value.Length == 0) return false;
        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0) return false;
            foreach (var character in identifier)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                {
                    return false;
                }
            }

            if (!allowLeadingZeroNumbers &&
                identifier.Length > 1 &&
                identifier[0] == '0' &&
                identifier.All(char.IsAsciiDigit))
            {
                return false;
            }
        }

        return true;
    }
}
