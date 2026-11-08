using System.Threading.Tasks;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.Core.Services;

/// <summary>
/// License stub. Licensing is disabled by default; when disabled the app behaves
/// exactly as today. No payment code, no server calls.
/// </summary>
public class LicenseService : ILicenseService
{
    public bool LicensingEnabled => false;
    public Task<bool> IsLicensedAsync() => Task.FromResult(true);
    public Task<bool> ActivateAsync(string licenseKey) => Task.FromResult(false);
    public string LicenseStatus => "Not licensed (feature disabled)";
}
