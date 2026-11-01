using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DraftEngine.Core.Licensing;

/// <summary>
/// Verifies signed license keys entirely offline. No network access is performed and the
/// private signing key never exists in the application.
/// </summary>
public sealed class LicenseKeyVerifier
{
    private readonly string _publicKeyPem;

    /// <summary>Creates a verifier from an RSA public key in PEM (SubjectPublicKeyInfo) form.</summary>
    /// <param name="publicKeyPem">The embedded release public key.</param>
    public LicenseKeyVerifier(string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            throw new ArgumentException("A license public key is required.", nameof(publicKeyPem));
        }

        // Fail fast at startup if the embedded key is malformed.
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        _publicKeyPem = publicKeyPem;
    }

    /// <summary>
    /// Verifies a key and evaluates its validity at <paramref name="now"/>.
    /// </summary>
    /// <param name="licenseKey">The key entered by the user or loaded from local storage.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="gracePeriod">Allowed offline time after expiry. Defaults to 14 days.</param>
    /// <param name="lastVerifiedUtc">The last successful local check, used to detect clock rollback.</param>
    public LicenseEvaluation Verify(
        string? licenseKey,
        DateTimeOffset now,
        TimeSpan? gracePeriod = null,
        DateTimeOffset? lastVerifiedUtc = null)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return new LicenseEvaluation(LicenseStatus.Missing, null, "No license has been activated.");
        }

        var parts = licenseKey.Trim().Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], LicenseKeyCodec.Prefix, StringComparison.Ordinal))
        {
            return new LicenseEvaluation(LicenseStatus.Malformed, null, "That does not look like a DraftEngine license key.");
        }

        if (!LicenseKeyCodec.TryBase64UrlDecode(parts[1], out var payloadBytes) ||
            !LicenseKeyCodec.TryBase64UrlDecode(parts[2], out var signatureBytes))
        {
            return new LicenseEvaluation(LicenseStatus.Malformed, null, "That does not look like a DraftEngine license key.");
        }

        var signedBytes = Encoding.ASCII.GetBytes($"{LicenseKeyCodec.Prefix}.{parts[1]}");
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(_publicKeyPem);
            if (!rsa.VerifyData(signedBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                return new LicenseEvaluation(
                    LicenseStatus.InvalidSignature,
                    null,
                    "This license key could not be verified. Check that you pasted the complete key.");
            }
        }
        catch (CryptographicException)
        {
            return new LicenseEvaluation(
                LicenseStatus.InvalidSignature,
                null,
                "This license key could not be verified. Check that you pasted the complete key.");
        }

        LicensePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, LicenseKeyCodec.JsonOptions);
        }
        catch (JsonException)
        {
            return new LicenseEvaluation(LicenseStatus.InvalidPayload, null, "This license key is not valid for DraftEngine.");
        }

        if (payload is null ||
            !string.Equals(payload.Product, "DraftEngine", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(payload.Tier) ||
            payload.IssuedAt == default)
        {
            return new LicenseEvaluation(LicenseStatus.InvalidPayload, null, "This license key is not valid for DraftEngine.");
        }

        // Allow a little clock skew for freshly issued keys.
        if (now < payload.IssuedAt.AddMinutes(-5))
        {
            return new LicenseEvaluation(
                LicenseStatus.NotYetValid,
                payload,
                $"This license becomes valid on {payload.IssuedAt.UtcDateTime:yyyy-MM-dd}.");
        }

        // If the machine clock was moved backwards substantially, an expired license could
        // otherwise appear valid forever. One day of tolerance covers timezone/NTP noise.
        if (lastVerifiedUtc is { } lastVerified && now < lastVerified.AddDays(-1))
        {
            return new LicenseEvaluation(
                LicenseStatus.ClockRollback,
                payload,
                "The system clock has moved backwards. Correct the date and time, then restart DraftEngine.");
        }

        if (payload.ExpiresAt is not { } expiresAt)
        {
            return new LicenseEvaluation(LicenseStatus.Valid, payload, "License active (never expires).");
        }

        if (now <= expiresAt)
        {
            return new LicenseEvaluation(
                LicenseStatus.Valid,
                payload,
                $"License active until {expiresAt.UtcDateTime:yyyy-MM-dd}.");
        }

        var grace = gracePeriod ?? TimeSpan.FromDays(14);
        if (grace > TimeSpan.Zero && now <= expiresAt.Add(grace))
        {
            var remaining = expiresAt.Add(grace) - now;
            var days = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
            return new LicenseEvaluation(
                LicenseStatus.InGrace,
                payload,
                $"License expired on {expiresAt.UtcDateTime:yyyy-MM-dd}. Offline grace period: {days} day(s) remaining.");
        }

        return new LicenseEvaluation(
            LicenseStatus.Expired,
            payload,
            $"This license expired on {expiresAt.UtcDateTime:yyyy-MM-dd}.");
    }
}
