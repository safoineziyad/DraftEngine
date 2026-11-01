using System;

namespace DraftEngine.Core.Licensing;

/// <summary>
/// The outcome of checking a license key against the embedded public key, the system clock
/// and the configured offline grace period.
/// </summary>
/// <param name="Status">The evaluation status.</param>
/// <param name="License">The signed payload, when the signature was valid.</param>
/// <param name="Message">A short, user-safe explanation (never contains key material).</param>
public sealed record LicenseEvaluation(LicenseStatus Status, LicensePayload? License, string Message)
{
    /// <summary>True when the application may run (valid or still inside grace).</summary>
    public bool IsUsable => Status is LicenseStatus.Valid or LicenseStatus.InGrace;

    /// <summary>The licensed tier, or <c>Free</c> when no usable license exists.</summary>
    public string Tier => License?.Tier ?? "Free";

    /// <summary>The license expiry, or <c>null</c> for a perpetual/unlicensed state.</summary>
    public DateTimeOffset? ExpiresAt => License?.ExpiresAt;

    /// <summary>The evaluation used when licensing is switched off.</summary>
    public static LicenseEvaluation Disabled { get; } =
        new(LicenseStatus.Disabled, null, "Licensing is disabled.");
}
