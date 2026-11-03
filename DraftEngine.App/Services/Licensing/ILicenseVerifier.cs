using System;
using DraftEngine.Core.Licensing;

namespace DraftEngine.App.Services.Licensing;

/// <summary>Small indirection so the verifier can be replaced in tests or future builds.</summary>
public interface ILicenseVerifier
{
    /// <summary>Verifies a signed key at the given time.</summary>
    LicenseEvaluation Verify(
        string? licenseKey,
        DateTimeOffset now,
        TimeSpan? gracePeriod = null,
        DateTimeOffset? lastVerifiedUtc = null);
}
