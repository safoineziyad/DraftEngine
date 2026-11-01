using System.Threading;
using System.Threading.Tasks;
using DraftEngine.Core.Licensing;

namespace DraftEngine.App.Services.Licensing;

/// <summary>
/// Loads and activates offline licenses. Nothing here performs network I/O.
/// </summary>
public interface ILicenseService
{
    /// <summary>True when <c>Features:LicensingEnabled</c> is on.</summary>
    bool IsEnabled { get; }

    /// <summary>The last evaluation, for display in the activation/About screens.</summary>
    LicenseEvaluation Current { get; }

    /// <summary>Reads the encrypted local activation and verifies it.</summary>
    Task<LicenseEvaluation> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Verifies, persists and applies a license key entered by the user.</summary>
    Task<LicenseEvaluation> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default);
}
