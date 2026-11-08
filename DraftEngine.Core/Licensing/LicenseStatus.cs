namespace DraftEngine.Core.Licensing;

/// <summary>
/// Result of evaluating a signed license key. Only <see cref="Valid"/> and
/// <see cref="InGrace"/> allow the licensed portion of the application to run.
/// </summary>
public enum LicenseStatus
{
    /// <summary>Licensing is disabled by configuration.</summary>
    Disabled,

    /// <summary>No license has been activated on this machine.</summary>
    Missing,

    /// <summary>The key is not in the expected DE1 payload.signature format.</summary>
    Malformed,

    /// <summary>The signature does not match the embedded public key.</summary>
    InvalidSignature,

    /// <summary>The payload is signed but contains invalid or unexpected data.</summary>
    InvalidPayload,

    /// <summary>The license's validity period has not started yet.</summary>
    NotYetValid,

    /// <summary>The license has expired and the offline grace period is over.</summary>
    Expired,

    /// <summary>The license has expired but is still inside the offline grace period.</summary>
    InGrace,

    /// <summary>The license is valid.</summary>
    Valid,

    /// <summary>The system clock is earlier than the last successful check, so expiry cannot be trusted.</summary>
    ClockRollback,
}
