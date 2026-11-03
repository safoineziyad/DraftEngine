using System.Threading.Tasks;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.Core.Services;

/// <summary>
/// In-process AI consent store. Consent is kept in memory for the session and
/// defaults to false (AI blocked until the user explicitly consents).
/// </summary>
public class AiConsentService : IAiConsentService
{
    private bool _consent;
    public Task<bool> HasConsentAsync() => Task.FromResult(_consent);
    public Task GrantConsentAsync() { _consent = true; return Task.CompletedTask; }
    public Task RevokeConsentAsync() { _consent = false; return Task.CompletedTask; }
}
