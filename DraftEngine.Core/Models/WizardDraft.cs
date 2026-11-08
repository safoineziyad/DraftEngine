namespace DraftEngine.Core.Models;

/// <summary>
/// Stores an encrypted in-progress contract-wizard payload.
/// </summary>
public class WizardDraft
{
    public string DraftKey { get; set; } = string.Empty;
    public string EncryptedPayload { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
