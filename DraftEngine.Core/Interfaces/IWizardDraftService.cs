using System.Threading.Tasks;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Persists an in-progress wizard draft so it survives navigation and application
/// restarts. Implementations are responsible for encrypting the payload at rest.
/// </summary>
public interface IWizardDraftService
{
    /// <summary>Loads the decrypted serialized draft, or an empty string when none exists.</summary>
    Task<string> LoadDraftAsync(string draftKey);

    /// <summary>Encrypts and persists a serialized draft.</summary>
    Task SaveDraftAsync(string draftKey, string payload);

    Task DeleteDraftAsync(string draftKey);
}
