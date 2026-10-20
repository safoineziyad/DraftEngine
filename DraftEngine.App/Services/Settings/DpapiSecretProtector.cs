using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace DraftEngine.App.Services.Settings;

/// <summary>
/// <see cref="ISecretProtector"/> implemented with Windows DPAPI
/// (<see cref="ProtectedData"/>), using <see cref="DataProtectionScope.CurrentUser"/> and a
/// fixed application entropy. This means ciphertext can ONLY be decrypted by the same Windows
/// user account on the same machine — copying settings.json to another user or machine makes
/// the stored API key permanently unreadable (it then degrades to "no key configured").
/// The protected output is base64; the plaintext never touches disk, logs or exceptions.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    // Application-specific entropy: prevents other DPAPI users of the same account from
    // accidentally decrypting our blobs, and domain-joins the ciphertext to DraftEngine.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DraftEngine.v1.OpenAiApiKey");

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(bytes);
        return Convert.ToBase64String(protectedBytes);
    }

    /// <inheritdoc />
    public string? Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
            return null;

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedValue);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var plaintext = Encoding.UTF8.GetString(bytes);
            CryptographicOperations.ZeroMemory(bytes);
            return plaintext;
        }
        catch (FormatException)
        {
            // Not valid base64 -> treat as unreadable, never crash.
            Log.Warning("Stored secret is not valid base64 and was ignored.");
            return null;
        }
        catch (CryptographicException)
        {
            // Decryption failed (different Windows user/machine, corrupted file, ...).
            Log.Warning("Stored secret could not be decrypted (different Windows user or machine?). It will be treated as missing.");
            return null;
        }
    }
}
