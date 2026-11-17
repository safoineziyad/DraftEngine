using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DraftEngine.Updates;

/// <summary>Installer descriptor carried by a signed manifest.</summary>
public sealed record UpdateInstaller(string Url, string FileName, long SizeBytes, string Sha256);

/// <summary>A parsed, validated update manifest (signature still needs verification).</summary>
public sealed class UpdateManifest
{
    /// <summary>Supported manifest schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of this document.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Release version in SemVer form.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>UTC publication timestamp.</summary>
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Optional oldest client version still supported.</summary>
    public string? MinimumSupportedVersion { get; init; }

    /// <summary>Release notes by language code (en/fr/ar/es).</summary>
    public IReadOnlyDictionary<string, string> ReleaseNotes { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The installer this manifest points at.</summary>
    public UpdateInstaller Installer { get; init; } = new(string.Empty, string.Empty, 0, string.Empty);

    /// <summary>Base64 ECDSA P-256 signature over the canonical manifest.</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>The parsed SemVer version; readers must only use validated manifests.</summary>
    public SemanticVersion ParsedVersion { get; init; } = SemanticVersion.Parse("0.0.0");

    /// <summary>True when the running client is below the manifest's minimum supported version.</summary>
    public bool IsRequiredFor(SemanticVersion current) =>
        MinimumSupportedVersion is not null &&
        SemanticVersion.TryParse(MinimumSupportedVersion, out var minimum) &&
        minimum.IsNewerThan(current);

    /// <summary>Release notes for the requested language, falling back to English.</summary>
    public string NotesFor(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode) &&
            ReleaseNotes.TryGetValue(NormalizeLanguage(languageCode), out var localized) &&
            !string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        return ReleaseNotes.TryGetValue("en", out var english) ? english : string.Empty;
    }

    private static string NormalizeLanguage(string code)
    {
        var separator = code.IndexOfAny(['-', '_']);
        return (separator > 0 ? code[..separator] : code).ToLowerInvariant();
    }
}

/// <summary>
/// Canonical UTF-8 serialization of a manifest without its signature. Field order, number
/// formatting, timestamp normalization and escaping are fixed so the signer and the verifier
/// always produce identical bytes.
/// </summary>
public static class ManifestCanonicalizer
{
    /// <summary>Returns the canonical JSON string (no signature field).</summary>
    public static string CanonicalJson(UpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var builder = new StringBuilder(512);
        builder.Append('{');
        builder.Append("\"schemaVersion\":").Append(manifest.SchemaVersion);
        builder.Append(",\"version\":").Append(Quote(manifest.Version));
        builder.Append(",\"publishedAt\":").Append(Quote(FormatTimestamp(manifest.PublishedAt)));

        if (!string.IsNullOrWhiteSpace(manifest.MinimumSupportedVersion))
        {
            builder.Append(",\"minimumSupportedVersion\":").Append(Quote(manifest.MinimumSupportedVersion));
        }

        builder.Append(",\"releaseNotes\":{");
        var first = true;
        foreach (var pair in manifest.ReleaseNotes.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!first) builder.Append(',');
            first = false;
            builder.Append(Quote(pair.Key)).Append(':').Append(Quote(pair.Value));
        }

        builder.Append('}');
        builder.Append(",\"installer\":{");
        builder.Append("\"url\":").Append(Quote(manifest.Installer.Url));
        builder.Append(",\"fileName\":").Append(Quote(manifest.Installer.FileName));
        builder.Append(",\"sizeBytes\":").Append(manifest.Installer.SizeBytes);
        builder.Append(",\"sha256\":").Append(Quote(manifest.Installer.Sha256));
        builder.Append("}}");
        return builder.ToString();
    }

    /// <summary>Returns the canonical UTF-8 bytes that are signed.</summary>
    public static byte[] CanonicalBytes(UpdateManifest manifest) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(CanonicalJson(manifest));

    /// <summary>Normalizes a timestamp to UTC with second precision.</summary>
    public static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static string Quote(string value) => JsonSerializer.Serialize(value);
}

/// <summary>Verifies a manifest signature against an embedded public key.</summary>
public interface IManifestVerifier
{
    /// <summary>True only when the signature matches the canonical manifest bytes.</summary>
    bool Verify(UpdateManifest manifest);
}

/// <summary>
/// ECDSA P-256 verifier. The constructor takes the public key in SubjectPublicKeyInfo format,
/// base64 encoded (the same text <c>tools/UpdateKeyTool keygen</c> prints for the app config).
/// </summary>
public sealed class EcdsaManifestVerifier : IManifestVerifier, IDisposable
{
    private readonly ECDsa _key;

    /// <summary>Imports the embedded public key.</summary>
    /// <exception cref="CryptographicException">The key is not a valid P-256 public key.</exception>
    public EcdsaManifestVerifier(string publicKeySpkiBase64)
    {
        if (string.IsNullOrWhiteSpace(publicKeySpkiBase64))
        {
            throw new ArgumentException("A public key is required.", nameof(publicKeySpkiBase64));
        }

        _key = ECDsa.Create();
        _key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeySpkiBase64.Trim()), out _);
    }

    /// <inheritdoc />
    public bool Verify(UpdateManifest manifest)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Signature))
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(manifest.Signature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            return _key.VerifyData(
                ManifestCanonicalizer.CanonicalBytes(manifest),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _key.Dispose();
}
