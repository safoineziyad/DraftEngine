using System.IO;

namespace DraftEngine.Updates;

/// <summary>Configuration for the update flow, supplied by the app (appsettings + settings page).</summary>
public sealed class UpdateOptions
{
    /// <summary>Default manifest URL (signed JSON at the latest release).</summary>
    public const string DefaultManifestUrl =
        "https://github.com/safoineziyad/DraftEngine/releases/latest/download/latest.json";

    /// <summary>When false, no request is made and the check reports <c>Disabled</c>.</summary>
    public bool ChecksEnabled { get; set; } = true;

    /// <summary>Stable-channel manifest URL.</summary>
    public string ManifestUrl { get; set; } = DefaultManifestUrl;

    /// <summary>Beta-channel manifest URL (empty means the beta channel is unavailable).</summary>
    public string BetaManifestUrl { get; set; } = string.Empty;

    /// <summary>Exact installer hosts allowed by policy.</summary>
    public string[] AllowedInstallerHosts { get; set; } = { "github.com", "objects.githubusercontent.com" };

    /// <summary>Embedded ECDSA P-256 public key (SPKI, base64); not a secret.</summary>
    public string PublicKeySpkiBase64 { get; set; } = string.Empty;

    /// <summary>Verify Authenticode in addition to the manifest hash (off until the installer is signed).</summary>
    public bool RequireAuthenticode { get; set; }

    /// <summary>User-Agent sent with update requests.</summary>
    public string UserAgent { get; set; } = "DraftEngine/0.0.0";

    /// <summary>Folder receiving verified installers and pre-update snapshots.</summary>
    public string UpdatesDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DraftEngine",
        "updates");

    /// <summary>Manifest request timeout (10 seconds by design), no retries.</summary>
    public TimeSpan ManifestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Minimum interval between automatic checks.</summary>
    public TimeSpan AutomaticCheckInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Delay before the automatic startup check, so it never blocks startup.</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>Stable release (default) or opt-in beta channel.</summary>
public enum UpdateChannel
{
    Stable,
    Beta,
}

/// <summary>Injectable clock so cadence can be tested without waiting.</summary>
public interface IUpdateClock
{
    /// <summary>Current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Wall-clock implementation.</summary>
public sealed class SystemUpdateClock : IUpdateClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Persistence for "last checked" and "skip this version". The app implements this on top of its
/// settings; tests use <see cref="InMemoryUpdateStateStore"/>.
/// </summary>
public interface IUpdateStateStore
{
    string? SkippedVersion { get; }
    DateTimeOffset? LastCheckedUtc { get; }
    void MarkChecked(DateTimeOffset checkedAt);
    void SkipVersion(string version);
}

/// <summary>In-memory state store (tests and previews).</summary>
public sealed class InMemoryUpdateStateStore : IUpdateStateStore
{
    /// <inheritdoc />
    public string? SkippedVersion { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? LastCheckedUtc { get; private set; }

    /// <inheritdoc />
    public void MarkChecked(DateTimeOffset checkedAt) => LastCheckedUtc = checkedAt;

    /// <inheritdoc />
    public void SkipVersion(string version) => SkippedVersion = version;
}
