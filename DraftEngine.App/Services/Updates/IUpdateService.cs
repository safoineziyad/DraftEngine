using System.Threading;
using System.Threading.Tasks;

namespace DraftEngine.App.Services.Updates;

/// <summary>Checks GitHub Releases and downloads a trusted DraftEngine installer.</summary>
public interface IUpdateService
{
    /// <summary>Returns the latest release when it is newer than the running application.</summary>
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads a release installer to the requested destination path.</summary>
    Task DownloadInstallerAsync(
        UpdateInfo update,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
