using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DraftEngine.App.Configuration;
using DraftEngine.Core.Updates;
using Serilog;

namespace DraftEngine.App.Services.Updates;

/// <summary>
/// Fetches a static HTTPS JSON manifest and compares its version with the running app.
/// No telemetry, identifiers, query strings or automatic downloads are used.
/// </summary>
public sealed class UpdateCheckService : IUpdateCheckService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AppOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>Creates the service.</summary>
    public UpdateCheckService(AppOptions options, IHttpClientFactory httpClientFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <inheritdoc />
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Features.UpdateCheckEnabled)
        {
            return null;
        }

        if (!TryGetManifestUri(_options.Features.UpdateManifestUrl, out var manifestUri))
        {
            Log.Warning("Update check is enabled but Features:UpdateManifestUrl is not a valid HTTPS URL.");
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("updates");
            using var response = await client.GetAsync(
                manifestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var manifest = await JsonSerializer
                .DeserializeAsync<UpdateManifest>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (manifest is null ||
                !UpdateVersionComparer.IsNewer(manifest.Version, AppVersion.Display))
            {
                return null;
            }

            if (!TryGetManifestUri(manifest.DownloadUrl, out _))
            {
                Log.Warning("Update manifest advertises version {Version} without a valid HTTPS download URL.", manifest.Version);
                return null;
            }

            return new UpdateInfo(
                manifest.Version.Trim(),
                manifest.DownloadUrl.Trim(),
                manifest.ReleaseNotes?.Trim() ?? string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Update checks are best-effort and must never disturb startup.
            Log.Warning(ex, "Notify-only update check failed.");
            return null;
        }
    }

    private static bool TryGetManifestUri(string? value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(parsed.UserInfo))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
