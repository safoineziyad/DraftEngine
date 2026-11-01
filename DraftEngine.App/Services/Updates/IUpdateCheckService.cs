using System.Threading;
using System.Threading.Tasks;

namespace DraftEngine.App.Services.Updates;

/// <summary>A newer release discovered in the static update manifest.</summary>
public sealed record UpdateInfo(
    string Version,
    string DownloadUrl,
    string ReleaseNotes);

/// <summary>
/// Notify-only update check. It reads one static JSON manifest and never downloads or
/// installs anything. When <c>Features:UpdateCheckEnabled</c> is off it performs no request.
/// </summary>
public interface IUpdateCheckService
{
    /// <summary>Returns the newer release, or null when up to date/disabled/unreachable.</summary>
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default);
}
