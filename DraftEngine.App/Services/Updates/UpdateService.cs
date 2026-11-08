using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DraftEngine.App.Configuration;
using DraftEngine.Core.Updates;
using Serilog;

namespace DraftEngine.App.Services.Updates;

/// <summary>Checks the DraftEngine GitHub Releases feed and downloads its installer assets.</summary>
public sealed class UpdateService : IUpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/safoineziyad/DraftEngine/releases/latest";
    private const string ReleaseAssetPathPrefix =
        "/safoineziyad/DraftEngine/releases/download/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AppOptions _options;
    private readonly HttpClient _httpClient;

    /// <summary>Creates the update service.</summary>
    public UpdateService(AppOptions options, HttpClient httpClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (Debugger.IsAttached || !_options.Features.AutoUpdateEnabled)
        {
            return null;
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                LatestReleaseUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            var version = release?.TagName?.Trim();
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
            if (string.IsNullOrWhiteSpace(version) ||
                !UpdateVersionComparer.IsNewer(version, currentVersion))
            {
                return null;
            }

            var asset = release?.Assets?.FirstOrDefault(candidate =>
                candidate.Name is not null &&
                candidate.Name.StartsWith("DraftEngine-Setup-", StringComparison.OrdinalIgnoreCase) &&
                candidate.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                TryGetTrustedAssetUri(candidate.BrowserDownloadUrl, out _));

            if (asset is null ||
                !TryGetTrustedAssetUri(asset.BrowserDownloadUrl, out var downloadUri))
            {
                Log.Warning("The latest GitHub release has no trusted DraftEngine installer asset.");
                return null;
            }

            return new UpdateInfo(version, downloadUri.AbsoluteUri, release?.Body?.Trim() ?? string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GitHub release update check failed.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DownloadInstallerAsync(
        UpdateInfo update,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!TryGetTrustedAssetUri(update.DownloadUrl, out var downloadUri))
        {
            throw new InvalidOperationException("The update does not contain a trusted DraftEngine installer URL.");
        }

        using var response = await _httpClient.GetAsync(
            downloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("The installer destination must include a directory.");
        }

        Directory.CreateDirectory(directory);
        await using (var source = await response.Content
                         .ReadAsStreamAsync(cancellationToken)
                         .ConfigureAwait(false))
        await using (var destination = new FileStream(
                         fullPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 81920,
                         useAsync: true))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        await using var downloadedFile = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 2,
            useAsync: true);
        var signature = new byte[2];
        var bytesRead = await downloadedFile.ReadAsync(signature.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        if (bytesRead != 2 || signature[0] != (byte)'M' || signature[1] != (byte)'Z')
        {
            File.Delete(fullPath);
            throw new InvalidDataException("The downloaded update is not a valid Windows executable.");
        }
    }

    private static bool TryGetTrustedAssetUri(string? value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !parsed.AbsolutePath.StartsWith(ReleaseAssetPathPrefix, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(parsed.AbsolutePath).StartsWith("DraftEngine-Setup-", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(parsed.AbsolutePath).EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; init; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }
    }
}
