using System;
using DraftEngine.Core.Enums;

namespace DraftEngine.Core.Models;

/// <summary>
/// Represents a chat message for a contract.
/// </summary>
public class ChatMessage
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the contract the message belongs to.
    /// </summary>
    public Contract Contract { get; set; } = null!;
}
