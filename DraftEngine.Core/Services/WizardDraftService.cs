using System.Threading.Tasks;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.Core.Services;

/// <summary>
/// Wizard draft persistence stub. Payloads are stored/encrypted by the caller;
/// this service persists the encrypted string keyed by draft key.
/// </summary>
public class WizardDraftService : IWizardDraftService
{
    public Task<string> LoadDraftAsync(string draftKey) => Task.FromResult<string>(null);
    public Task SaveDraftAsync(string draftKey, string encryptedPayload) => Task.CompletedTask;
    public Task DeleteDraftAsync(string draftKey) => Task.CompletedTask;
}
