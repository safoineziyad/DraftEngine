using System.Threading.Tasks;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Offline license verification behind a feature flag. When LicensingEnabled is
/// false the app behaves exactly as today; no payment code and no server calls.
/// </summary>
public interface ILicenseService
{
    bool LicensingEnabled { get; }
    Task<bool> IsLicensedAsync();
    Task<bool> ActivateAsync(string licenseKey);
    string LicenseStatus { get; }
}
