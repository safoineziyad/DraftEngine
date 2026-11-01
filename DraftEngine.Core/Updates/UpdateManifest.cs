namespace DraftEngine.Core.Updates;

/// <summary>
/// The static JSON document read by the notify-only update check.
/// Example:
/// <code>
/// {
///   "version": "0.3.0",
///   "downloadUrl": "https://example.com/draftengine/download",
///   "releaseNotes": "Adds the new contract types."
/// }
/// </code>
/// </summary>
public sealed class UpdateManifest
{
    /// <summary>The latest released version, e.g. <c>0.3.0</c>.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>HTTPS page the user can open to download the release.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Optional short release notes shown in the notification.</summary>
    public string ReleaseNotes { get; set; } = string.Empty;
}
