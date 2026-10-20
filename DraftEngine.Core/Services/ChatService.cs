using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Core.Services;

/// <summary>
/// Persists and reads the per-contract chat history. Messages are stored in creation order
/// and returned oldest first so the chat panel renders chronologically.
/// </summary>
public class ChatService : IChatService
{
    private readonly DbContext _database;

    /// <summary>Creates the chat service.</summary>
    /// <param name="database">EF Core context hosting the DraftEngine model.</param>
    public ChatService(DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public async Task<ChatMessage> AddMessageAsync(int contractId, MessageRole role, string content)
    {
        var message = new ChatMessage
        {
            ContractId = contractId,
            Role = role,
            Content = content,
            CreatedAt = DateTime.UtcNow,
        };

        _database.Set<ChatMessage>().Add(message);
        await _database.SaveChangesAsync();
        return message;
    }

    /// <inheritdoc />
    public async Task<List<ChatMessage>> GetMessagesAsync(int contractId) =>
        await _database.Set<ChatMessage>()
            .AsNoTracking()
            .Where(message => message.ContractId == contractId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync();
}
