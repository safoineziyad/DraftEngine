using System.Collections.Generic;
using System.Threading.Tasks;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Service for managing chat messages.
/// </summary>
public interface IChatService
{
    Task<ChatMessage> AddMessageAsync(int contractId, MessageRole role, string content);
    Task<List<ChatMessage>> GetMessagesAsync(int contractId);
}
