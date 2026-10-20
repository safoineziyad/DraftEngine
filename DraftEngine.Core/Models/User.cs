using System;

namespace DraftEngine.Core.Models;

/// <summary>
/// Represents a user of the application.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string OpenAiApiKeyEncrypted { get; set; } = string.Empty;
    public string SubscriptionTier { get; set; } = "Free";
    public DateTime? SubscriptionExpiresAt { get; set; }

    /// <summary>
    /// Gets the contracts owned by the user.
    /// </summary>
    public ICollection<Contract> Contracts { get; set; } = new List<Contract>();
}
