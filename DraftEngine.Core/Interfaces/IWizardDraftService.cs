using System.Threading.Tasks;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Persists an in-progress wizard draft (encrypted payload) so it survives
/// navigation and application restarts.
/// </summary>
public interface IWizardDraftService
{
    Task<string> LoadDraftAsync(string draftKey);
    Task SaveDraftAsync(string draftKey, string encryptedPayload);
    Task DeleteDraftAsync(string draftKey);
}
