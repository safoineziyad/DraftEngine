using System.Globalization;
using System.IO;
using System.Linq;

namespace DraftEngine.App.Services;

/// <summary>
/// Turns a user-supplied title into a writable Windows file name. Sanitization is
/// culture-independent: reserved device names are matched with ordinal casing, and length
/// limits are measured in characters, so the result is identical under every UI language.
/// </summary>
public static class ExportFileNameSanitizer
{
    /// <summary>Maximum stem length, leaving room for a directory and the extension.</summary>
    public const int MaxStemLength = 120;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Sanitizes a bare file name and appends <paramref name="extension"/>.</summary>
    public static string Sanitize(string? rawName, string extension = "docx")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((rawName ?? string.Empty)
            .Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character)
            .ToArray())
            .Trim(' ', '.');

        if (safe.Length > MaxStemLength)
        {
            safe = safe[..MaxStemLength].TrimEnd(' ', '.');
        }

        if (safe.Length == 0)
        {
            safe = "Contract";
        }

        var stem = Path.GetFileNameWithoutExtension(safe);
        if (ReservedNames.Contains(stem))
        {
            safe = "_" + safe;
        }

        // Extension is normalized to lower case invariantly; the stem keeps the user's casing.
        return $"{safe}.{extension.ToLowerInvariant()}";
    }

    /// <summary>True when the sanitized name is safe on Windows (used by tests and diagnostics).</summary>
    public static bool IsSafe(string fileName) =>
        fileName.Length > 0 &&
        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !ReservedNames.Contains(Path.GetFileNameWithoutExtension(fileName)) &&
        fileName == fileName.Trim() &&
        !fileName.EndsWith('.') &&
        CultureInfo.InvariantCulture.TextInfo.ToLower(fileName) == fileName.ToLowerInvariant();
}
