using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DraftEngine.Core.Licensing;

/// <summary>
/// Creates and verifies the compact offline license format:
/// <c>DE1.&lt;base64url(payload-json)&gt;.&lt;base64url(rsa-sha256-signature)&gt;</c>.
/// The signature covers the ASCII prefix and the encoded payload exactly as issued.
/// </summary>
public static class LicenseKeyCodec
{
    /// <summary>Format/version prefix at the start of every license key.</summary>
    public const string Prefix = "DE1";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Signs a payload with an RSA private key. Intended for an offline license-issuing
    /// tool; the application itself only embeds the matching public key.
    /// </summary>
    public static string CreateSignedLicense(LicensePayload payload, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(privateKey);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var signedBytes = Encoding.ASCII.GetBytes($"{Prefix}.{encodedPayload}");
        var signature = privateKey.SignData(signedBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{Prefix}.{encodedPayload}.{Base64UrlEncode(signature)}";
    }

    internal static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static bool TryBase64UrlDecode(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        var remainder = padded.Length % 4;
        padded = remainder switch
        {
            2 => padded + "==",
            3 => padded + "=",
            0 => padded,
            _ => string.Empty,
        };

        if (padded.Length == 0)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
