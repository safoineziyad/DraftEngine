using System.Threading.Tasks;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Tracks AI consent: whether the user has allowed AI chat editing, and whether
/// they have revoked it. Stored locally; AI must not be called without consent.
/// </summary>
public interface IAiConsentService
{
    Task<bool> HasConsentAsync();
    Task GrantConsentAsync();
    Task RevokeConsentAsync();
}
