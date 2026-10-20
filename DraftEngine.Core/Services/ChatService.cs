using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Core.Services;

/// <summary>
/// Persists and reads the per-contract chat history. Messages are stored in creation order
/// and returned oldest first so the chat panel renders chronologically. Stateless: each
/// operation opens its own short-lived context via <see cref="IDatabaseContextFactory"/>.
/// </summary>
public class ChatService : IChatService
{
    private readonly IDatabaseContextFactory _databaseFactory;

    /// <summary>Creates the chat service.</summary>
    /// <param name="databaseFactory">Factory handing out one short-lived context per operation.</param>
    public ChatService(IDatabaseContextFactory databaseFactory)
    {
        ArgumentNullException.ThrowIfNull(databaseFactory);
        _databaseFactory = databaseFactory;
    }

    /// <inheritdoc />
    public async Task<ChatMessage> AddMessageAsync(int contractId, MessageRole role, string content)
    {
        using var database = _databaseFactory.CreateContext();

        var message = new ChatMessage
        {
            ContractId = contractId,
            Role = role,
            Content = content,
            CreatedAt = DateTime.UtcNow,
        };

        database.Set<ChatMessage>().Add(message);
        await database.SaveChangesAsync();
        return message;
    }

    /// <inheritdoc />
    public async Task<List<ChatMessage>> GetMessagesAsync(int contractId)
    {
        using var database = _databaseFactory.CreateContext();

        return await database.Set<ChatMessage>()
            .AsNoTracking()
            .Where(message => message.ContractId == contractId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync();
    }
}
