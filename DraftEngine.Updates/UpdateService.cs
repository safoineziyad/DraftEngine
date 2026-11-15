using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace DraftEngine.Updates;

/// <summary>Outcome of one update check.</summary>
public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    Skipped,
    Throttled,
    Disabled,
    Error,
}

/// <summary>Result of a check; <see cref="Manifest"/> is set only when an update is available.</summary>
public sealed class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; init; }
    public UpdateManifest? Manifest { get; init; }
    public UpdateError? Error { get; init; }
    public DateTimeOffset CheckedAt { get; init; }

    /// <summary>True when a newer, verified release should be offered.</summary>
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable && Manifest is not null;
}

/// <summary>Result of a verified download.</summary>
public sealed record UpdateDownloadResult(bool Success, string? FilePath, long SizeBytes, UpdateError? Error)
{
    public static UpdateDownloadResult Failed(UpdateErrorCode code) => new(false, null, 0, UpdateError.From(code));
}

/// <summary>Result of a launch attempt.</summary>
public sealed record UpdateInstallResult(bool Success, UpdateError? Error)
{
    public static UpdateInstallResult Failed(UpdateErrorCode code) => new(false, UpdateError.From(code));
}

/// <summary>Starts the downloaded installer; injectable so tests never spawn a process.</summary>
public interface IInstallerLauncher
{
    Task<bool> LaunchAsync(string installerPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Inno Setup launcher. <c>/SILENT</c> shows progress without asking questions, and Inno closes
/// the running app and restarts it afterwards. This is the only place an installer is started.
/// </summary>
public sealed class ProcessInstallerLauncher : IInstallerLauncher
{
    /// <summary>The exact switches used for every update install.</summary>
    public const string Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";

    /// <inheritdoc />
    public Task<bool> LaunchAsync(string installerPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = Arguments,
                UseShellExecute = true,
            });
            return Task.FromResult(process is not null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // Includes ERROR_CANCELLED when the user declines the UAC prompt.
            return Task.FromResult(false);
        }
    }
}

/// <summary>Authenticode check for the downloaded installer (WinVerifyTrust).</summary>
public interface IAuthenticodeVerifier
{
    bool IsValid(string filePath);
}

/// <summary>
/// Signs are not present yet, so this is only consulted when <c>RequireAuthenticode</c> is on.
/// On non-Windows hosts it returns false.
/// </summary>
public sealed class WinVerifyTrustAuthenticodeVerifier : IAuthenticodeVerifier
{
    /// <inheritdoc />
    public bool IsValid(string filePath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(filePath))
        {
            return false;
        }

        return Authenticode.IsSignedByTrustedPublisher(filePath);
    }
}

/// <summary>
/// Checks a signed manifest, downloads and verifies the installer, and launches it only when the
/// caller confirms. Never installs anything by itself and never touches the network when disabled.
/// </summary>
public sealed class UpdateService
{
    private const long MaxManifestBytes = 1024 * 1024;
    private const int ErrorDiskFull = 112;
    private const int ErrorHandleDiskFull = 39;

    private readonly HttpClient _httpClient;
    private readonly UpdateOptions _options;
    private readonly IUpdateStateStore _state;
    private readonly IUpdateClock _clock;
    private readonly IManifestVerifier _verifier;
    private readonly IInstallerLauncher _launcher;
    private readonly IAuthenticodeVerifier? _authenticode;
    private readonly SemanticVersion _currentVersion;
    private readonly Action<string>? _log;

    /// <summary>Creates the service for the running app version (informational, SemVer).</summary>
    public UpdateService(
        HttpClient httpClient,
        UpdateOptions options,
        IUpdateStateStore state,
        IUpdateClock clock,
        IManifestVerifier verifier,
        SemanticVersion currentVersion,
        IInstallerLauncher? launcher = null,
        IAuthenticodeVerifier? authenticode = null,
        Action<string>? log = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        _launcher = launcher ?? new ProcessInstallerLauncher();
        _authenticode = authenticode;
        _log = log;
    }

    /// <summary>
    /// Checks the selected channel. Automatic checks are throttled to one per
    /// <see cref="UpdateOptions.AutomaticCheckInterval"/>; manual checks always fetch.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(
        UpdateChannel channel = UpdateChannel.Stable,
        bool manual = false,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        if (!_options.ChecksEnabled)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Disabled, CheckedAt = now, Error = UpdateError.From(UpdateErrorCode.Disabled) };
        }

        if (!manual && _state.LastCheckedUtc is { } last && now - last < _options.AutomaticCheckInterval)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Throttled, CheckedAt = now };
        }

        var url = channel == UpdateChannel.Beta ? _options.BetaManifestUrl : _options.ManifestUrl;
        if (!UpdateHostPolicy.IsHttpsUri(url, out var manifestUri))
        {
            _state.MarkChecked(now);
            _log?.Invoke("update check failed: BlockedHost");
            return Error(UpdateErrorCode.BlockedHost, now);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ManifestTimeout);

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode == HttpStatusCode.NotFound
                    ? UpdateErrorCode.NotFound
                    : UpdateErrorCode.HttpError;
                _state.MarkChecked(now);
                _log?.Invoke($"update check failed: {code} ({(int)response.StatusCode})");
                return Error(code, now);
            }

            var json = await ReadLimitedAsync(response, timeout.Token).ConfigureAwait(false);
            if (!UpdateManifestParser.TryParse(json, new UpdateHostPolicy(_options.AllowedInstallerHosts), out var manifest, out var parseError))
            {
                _state.MarkChecked(now);
                _log?.Invoke($"update check failed: {parseError!.Code}");
                return new UpdateCheckResult { Status = UpdateCheckStatus.Error, Error = parseError, CheckedAt = now };
            }

            if (!_verifier.Verify(manifest))
            {
                _state.MarkChecked(now);
                _log?.Invoke($"update check failed: InvalidSignature (version {manifest.Version})");
                return Error(UpdateErrorCode.InvalidSignature, now);
            }

            if (channel == UpdateChannel.Stable && manifest.ParsedVersion.IsPrerelease)
            {
                _state.MarkChecked(now);
                return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate, CheckedAt = now };
            }

            if (manifest.ParsedVersion.CompareTo(_currentVersion) < 0)
            {
                _state.MarkChecked(now);
                _log?.Invoke($"update check refused downgrade to {manifest.Version}");
                return Error(UpdateErrorCode.Downgrade, now);
            }

            if (!manifest.ParsedVersion.IsNewerThan(_currentVersion))
            {
                _state.MarkChecked(now);
                return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate, CheckedAt = now };
            }

            _state.MarkChecked(now);
            if (!string.IsNullOrWhiteSpace(_state.SkippedVersion) &&
                string.Equals(_state.SkippedVersion.Trim(), manifest.Version.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.Skipped, Manifest = manifest, CheckedAt = now };
            }

            _log?.Invoke($"update available: {manifest.Version}");
            return new UpdateCheckResult { Status = UpdateCheckStatus.UpdateAvailable, Manifest = manifest, CheckedAt = now };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _state.MarkChecked(now);
            _log?.Invoke("update check failed: Timeout");
            return Error(UpdateErrorCode.Timeout, now);
        }
        catch (HttpRequestException)
        {
            _state.MarkChecked(now);
            _log?.Invoke("update check failed: Offline");
            return Error(UpdateErrorCode.Offline, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _state.MarkChecked(now);
            _log?.Invoke($"update check failed: {ex.GetType().Name}");
            return Error(UpdateErrorCode.Offline, now);
        }
    }

    /// <summary>
    /// Streams the installer to a <c>.partial</c> file, verifies size and SHA-256 (and Authenticode
    /// when required), then moves it into the updates folder. A failure leaves nothing behind.
    /// </summary>
    public async Task<UpdateDownloadResult> DownloadAsync(
        UpdateManifest manifest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));

        if (!new UpdateHostPolicy(_options.AllowedInstallerHosts).IsAllowedInstallerUrl(manifest.Installer.Url, out var installerUri))
        {
            return UpdateDownloadResult.Failed(UpdateErrorCode.BlockedHost);
        }

        var directory = _options.UpdatesDirectory;
        var finalPath = Path.Combine(directory, manifest.Installer.FileName);
        var partialPath = finalPath + ".partial";

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UpdateDownloadResult.Failed(MapIoFailure(ex));
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                installerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateDownloadResult.Failed(UpdateErrorCode.HttpError);
            }

            var contentLength = response.Content.Headers.ContentLength;
            var total = contentLength ?? manifest.Installer.SizeBytes;
            long written = 0;
            string actualHash;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                             partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    written += read;
                    if (total > 0)
                    {
                        progress?.Report(Math.Min(1d, (double)written / total));
                    }
                }

                actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            if ((contentLength is { } declared && written != declared) || written != manifest.Installer.SizeBytes)
            {
                DeleteQuietly(partialPath);
                return UpdateDownloadResult.Failed(UpdateErrorCode.TruncatedDownload);
            }

            if (!string.Equals(actualHash, manifest.Installer.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                DeleteQuietly(partialPath);
                return UpdateDownloadResult.Failed(UpdateErrorCode.HashMismatch);
            }

            if (_options.RequireAuthenticode && (_authenticode is null || !_authenticode.IsValid(partialPath)))
            {
                DeleteQuietly(partialPath);
                return UpdateDownloadResult.Failed(UpdateErrorCode.AuthenticodeInvalid);
            }

            File.Move(partialPath, finalPath, overwrite: true);
            progress?.Report(1d);
            return new UpdateDownloadResult(true, finalPath, written, null);
        }
        catch (OperationCanceledException)
        {
            DeleteQuietly(partialPath);
            return UpdateDownloadResult.Failed(UpdateErrorCode.Cancelled);
        }
        catch (HttpRequestException)
        {
            DeleteQuietly(partialPath);
            return UpdateDownloadResult.Failed(UpdateErrorCode.Offline);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(partialPath);
            return UpdateDownloadResult.Failed(MapIoFailure(ex));
        }
    }

    /// <summary>
    /// Takes a snapshot (when a callback is supplied) and launches the installer. Callers must
    /// obtain explicit user confirmation before invoking this method.
    /// </summary>
    public async Task<UpdateInstallResult> InstallAsync(
        string installerPath,
        Func<Task>? takeDatabaseSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            return UpdateInstallResult.Failed(UpdateErrorCode.InstallerLaunchFailed);
        }

        if (takeDatabaseSnapshot is not null)
        {
            try
            {
                await takeDatabaseSnapshot().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"pre-update snapshot failed: {ex.GetType().Name}");
                return UpdateInstallResult.Failed(UpdateErrorCode.SnapshotFailed);
            }
        }

        bool launched;
        try
        {
            launched = await _launcher.LaunchAsync(installerPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            launched = false;
        }

        if (!launched)
        {
            _log?.Invoke("installer launch failed");
            return UpdateInstallResult.Failed(UpdateErrorCode.InstallerLaunchFailed);
        }

        return new UpdateInstallResult(true, null);
    }

    /// <summary>Records an explicit "skip this version" choice.</summary>
    public void SkipVersion(UpdateManifest manifest)
    {
        if (manifest is not null && !string.IsNullOrWhiteSpace(manifest.Version))
        {
            _state.SkipVersion(manifest.Version.Trim());
        }
    }

    private UpdateCheckResult Error(UpdateErrorCode code, DateTimeOffset now) =>
        new() { Status = UpdateCheckStatus.Error, Error = UpdateError.From(code), CheckedAt = now };

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[8192];
        using var memory = new MemoryStream();
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > MaxManifestBytes)
            {
                throw new InvalidDataException("Manifest is too large.");
            }

            memory.Write(buffer, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    private static UpdateErrorCode MapIoFailure(Exception ex)
    {
        var code = ex.HResult & 0xFFFF;
        return code is ErrorDiskFull or ErrorHandleDiskFull ? UpdateErrorCode.DiskFull : UpdateErrorCode.HttpError;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a locked partial file must not mask the original failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
