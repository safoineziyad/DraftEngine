using System.Text.Json;
using System.Text.RegularExpressions;

namespace DraftEngine.Updates;

/// <summary>
/// Transport and host policy: HTTPS only, no user-info, exact-host allowlist for installers.
/// </summary>
public sealed class UpdateHostPolicy
{
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled);
    private readonly HashSet<string> _installerHosts;

    /// <summary>Creates the policy from the configured allowlist.</summary>
    public UpdateHostPolicy(IEnumerable<string>? installerHosts = null)
    {
        _installerHosts = new HashSet<string>(
            installerHosts ?? new[] { "github.com", "objects.githubusercontent.com" },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The allowlisted installer hosts.</summary>
    public IReadOnlyCollection<string> InstallerHosts => _installerHosts;

    /// <summary>True for a plain HTTPS URL without user-info (used for manifest URLs).</summary>
    public static bool IsHttpsUri(string? value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            string.IsNullOrEmpty(parsed.Host))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>True when the URL is HTTPS and the exact host is allowlisted for installers.</summary>
    public bool IsAllowedInstallerUrl(string? value, out Uri uri) =>
        IsHttpsUri(value, out uri) && _installerHosts.Contains(uri.Host);

    /// <summary>True for a plain 64-character lowercase SHA-256 hex string.</summary>
    public static bool IsSha256Hex(string? value) => value is not null && Sha256Pattern.IsMatch(value);
}

/// <summary>Parses and validates update manifests; never verifies the signature (see <see cref="IManifestVerifier"/>).</summary>
public static class UpdateManifestParser
{
    /// <summary>
    /// Parses <paramref name="json"/> and validates every field. Returns false with a typed error for
    /// malformed JSON, unsupported schema, bad versions, timestamps, hashes, file names or blocked hosts.
    /// </summary>
    public static bool TryParse(string? json, UpdateHostPolicy hostPolicy, out UpdateManifest manifest, out UpdateError? error)
    {
        manifest = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = UpdateError.From(UpdateErrorCode.MalformedManifest);
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            error = UpdateError.From(UpdateErrorCode.MalformedManifest);
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = UpdateError.From(UpdateErrorCode.MalformedManifest);
                return false;
            }

            if (!TryGetInt(root, "schemaVersion", out var schema))
            {
                error = UpdateError.From(UpdateErrorCode.MalformedManifest);
                return false;
            }

            if (schema != UpdateManifest.CurrentSchemaVersion)
            {
                error = UpdateError.From(UpdateErrorCode.UnsupportedSchema);
                return false;
            }

            if (!TryGetString(root, "version", out var versionText) ||
                !SemanticVersion.TryParse(versionText, out var version))
            {
                error = UpdateError.From(UpdateErrorCode.MalformedManifest);
                return false;
            }

            if (!TryGetString(root, "publishedAt", out var publishedText) ||
                !DateTimeOffset.TryParse(
                    publishedText,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var publishedAt))
            {
                error = UpdateError.From(UpdateErrorCode.MalformedManifest);
                return false;
            }

            string? minimumSupported = null;
            if (root.TryGetProperty("minimumSupportedVersion", out var minimum) && minimum.ValueKind != JsonValueKind.Null)
            {
                if (minimum.ValueKind != JsonValueKind.String ||
                    !SemanticVersion.TryParse(minimum.GetString(), out _))
                {
                    error = UpdateError.From(UpdateErrorCode.MalformedManifest);
                    return false;
                }

                minimumSupported = minimum.GetString()!.Trim();
            }

            if (!TryReadNotes(root, out var notes))
            {
                error = UpdateError.From(UpdateErrorCode.MalformedManifest);
                return false;
            }

            if (!root.TryGetProperty("installer", out var installerElement) ||
                installerElement.ValueKind != JsonValueKind.Object ||
                !TryGetString(installerElement, "url", out var installerUrl) ||
                !TryGetString(installerElement, "fileName", out var fileName) ||
                !TryGetLong(installerElement, "sizeBytes", out var sizeBytes) ||
                !TryGetString(installerElement, "sha256", out var sha256))
            {
                error = UpdateError.From(UpdateErrorCode.MalformedManifest);
                return false;
            }

            if (!UpdateHostPolicy.IsSha256Hex(sha256.Trim().ToLowerInvariant()) || sizeBytes <= 0)
            {
                error = UpdateError.From(UpdateErrorCode.MalformedManifest);
                return false;
            }

            if (!IsLeafExecutableName(fileName))
            {
                error = UpdateError.From(UpdateErrorCode.MalformedManifest);
                return false;
            }

            if (!hostPolicy.IsAllowedInstallerUrl(installerUrl, out _))
            {
                error = UpdateError.From(UpdateErrorCode.BlockedHost);
                return false;
            }

            if (!TryGetString(root, "signature", out var signature) || string.IsNullOrWhiteSpace(signature))
            {
                error = UpdateError.From(UpdateErrorCode.InvalidSignature);
                return false;
            }

            manifest = new UpdateManifest
            {
                SchemaVersion = schema,
                Version = versionText.Trim(),
                ParsedVersion = version,
                PublishedAt = publishedAt,
                MinimumSupportedVersion = minimumSupported,
                ReleaseNotes = notes,
                Installer = new UpdateInstaller(
                    installerUrl.Trim(),
                    fileName.Trim(),
                    sizeBytes,
                    sha256.Trim().ToLowerInvariant()),
                Signature = signature.Trim(),
            };
            return true;
        }
    }

    private static bool IsLeafExecutableName(string fileName)
    {
        var trimmed = fileName.Trim();
        return trimmed.Length > 0 &&
               trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
               trimmed.IndexOfAny(['/', '\\']) < 0 &&
               trimmed.IndexOf(':') < 0 &&
               string.Equals(Path.GetFileName(trimmed), trimmed, StringComparison.Ordinal);
    }

    private static bool TryReadNotes(JsonElement root, out IReadOnlyDictionary<string, string> notes)
    {
        notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("releaseNotes", out var element))
        {
            return true; // optional; NotesFor falls back to empty
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var key = property.Name.Trim().ToLowerInvariant();
            if (key.Length == 0)
            {
                return false;
            }

            map[key] = property.Value.GetString() ?? string.Empty;
        }

        notes = map;
        return true;
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }

    private static bool TryGetLong(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out value);
    }
}
