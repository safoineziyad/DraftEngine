using System.Threading.Tasks;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Notify-only update check against a static JSON manifest URL (off by default).
/// No silent install, no telemetry.
/// </summary>
public interface IUpdateCheckService
{
    bool Enabled { get; }
    Task<string> CheckForUpdatesAsync();
}
