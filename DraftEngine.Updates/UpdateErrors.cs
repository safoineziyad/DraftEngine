namespace DraftEngine.Updates;

/// <summary>Every way an update check, download or launch can fail.</summary>
public enum UpdateErrorCode
{
    None,
    Disabled,
    Offline,
    Timeout,
    NotFound,
    HttpError,
    MalformedManifest,
    UnsupportedSchema,
    InvalidSignature,
    Downgrade,
    BlockedHost,
    HashMismatch,
    TruncatedDownload,
    DiskFull,
    AuthenticodeInvalid,
    SnapshotFailed,
    InstallerLaunchFailed,
    Cancelled,
    Generic,
}

/// <summary>A failure with a stable code and a resource key the UI can localize.</summary>
public sealed record UpdateError(UpdateErrorCode Code, string ResourceKey)
{
    /// <summary>Maps a code to its localized-message key (Strings.resx, all four languages).</summary>
    public static string KeyFor(UpdateErrorCode code) => code switch
    {
        UpdateErrorCode.Disabled => "Update.Error.Disabled",
        UpdateErrorCode.Offline => "Update.Error.Offline",
        UpdateErrorCode.Timeout => "Update.Error.Timeout",
        UpdateErrorCode.NotFound => "Update.Error.NotFound",
        UpdateErrorCode.HttpError => "Update.Error.Http",
        UpdateErrorCode.MalformedManifest => "Update.Error.Malformed",
        UpdateErrorCode.UnsupportedSchema => "Update.Error.Schema",
        UpdateErrorCode.InvalidSignature => "Update.Error.Signature",
        UpdateErrorCode.Downgrade => "Update.Error.Downgrade",
        UpdateErrorCode.BlockedHost => "Update.Error.BlockedHost",
        UpdateErrorCode.HashMismatch => "Update.Error.Hash",
        UpdateErrorCode.TruncatedDownload => "Update.Error.Truncated",
        UpdateErrorCode.DiskFull => "Update.Error.DiskFull",
        UpdateErrorCode.AuthenticodeInvalid => "Update.Error.Authenticode",
        UpdateErrorCode.SnapshotFailed => "Update.Error.Snapshot",
        UpdateErrorCode.InstallerLaunchFailed => "Update.Error.Launch",
        UpdateErrorCode.Cancelled => "Update.Error.Cancelled",
        _ => "Update.Error.Generic",
    };

    /// <summary>Creates an error from a code using the standard key map.</summary>
    public static UpdateError From(UpdateErrorCode code) => new(code, KeyFor(code));
}
