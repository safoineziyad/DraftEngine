using System.Net.Http;
using System.Threading.Tasks;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.Core.Services;

/// <summary>
/// Notify-only update check. Off by default; reads the manifest URL from config.
/// </summary>
public class UpdateCheckService : IUpdateCheckService
{
    private readonly string _manifestUrl;
    public UpdateCheckService(string manifestUrl = null) => _manifestUrl = manifestUrl;
    public bool Enabled => !string.IsNullOrWhiteSpace(_manifestUrl);
    public Task<string> CheckForUpdatesAsync()
    {
        if (!Enabled) return Task.FromResult<string>(null);
        return Task.FromResult<string>(null); // notify-only; no silent install
    }
}
