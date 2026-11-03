using System;
using DraftEngine.Core.Licensing;

namespace DraftEngine.App.Services.Licensing;

/// <summary>Adapter that verifies keys with the embedded release public key.</summary>
public sealed class EmbeddedLicenseVerifier : ILicenseVerifier
{
    private readonly LicenseKeyVerifier _verifier = new(LicensePublicKey.Pem);

    /// <inheritdoc />
    public LicenseEvaluation Verify(
        string? licenseKey,
        DateTimeOffset now,
        TimeSpan? gracePeriod = null,
        DateTimeOffset? lastVerifiedUtc = null) =>
        _verifier.Verify(licenseKey, now, gracePeriod, lastVerifiedUtc);
}
