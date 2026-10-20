namespace DraftEngine.App.Services.Settings;

/// <summary>
/// Protects and unprotects small secret strings (such as the OpenAI API key) at rest.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypts a plaintext secret into a reversible, non-reversible-looking string.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a value produced by <see cref="Protect"/>. Returns null when the value cannot be decrypted.</summary>
    string? Unprotect(string protectedValue);
}
