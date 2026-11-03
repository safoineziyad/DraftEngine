using System;

namespace DraftEngine.Core.Licensing;

/// <summary>
/// The signed portion of an offline license key. The values are informational and are
/// mirrored into the local <c>User.SubscriptionTier</c> / <c>User.SubscriptionExpiresAt</c>
/// fields after activation; the signature is the source of truth.
/// </summary>
public sealed class LicensePayload
{
    /// <summary>Stable identifier for the issued license (for support, not validation).</summary>
    public string LicenseId { get; set; } = string.Empty;

    /// <summary>The licensed person or company.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Contact email associated with the license.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Product identifier. Must be <c>DraftEngine</c>.</summary>
    public string Product { get; set; } = "DraftEngine";

    /// <summary>Tier mirrored into <c>User.SubscriptionTier</c> (e.g. Pro).</summary>
    public string Tier { get; set; } = "Pro";

    /// <summary>When the license was issued.</summary>
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the license expires. <c>null</c> means a perpetual license.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
