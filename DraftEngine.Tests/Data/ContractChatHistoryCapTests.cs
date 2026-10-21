using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests that the chat history stored per contract stays bounded (audit Finding #7):
/// <see cref="ContractService"/> keeps only the newest 200 messages after a chat edit.
/// </summary>
public class ContractChatHistoryCapTests : IDisposable
{
    private const int MaxChatMessagesPerContract = 200;

    private readonly SqliteConnection _connection;
    private readonly DraftEngineDbContext _dbContext;
    private readonly ContractService _service;
    private readonly FakeAi _fakeAi = new();

    public ContractChatHistoryCapTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(options);
        _dbContext.Database.EnsureCreated();

        _service = new ContractService(
            new TestDatabaseContextFactory(options), new FieldService(), _fakeAi);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ApplyChatEditAsync_BeyondTheCap_KeepsOnlyTheNewestMessages()
    {
        var contract = new Contract
        {
            User = new User { Email = "dev@example.com", DisplayName = "Dev" },
            Title = "Test NDA",
            ContractType = "Mutual NDA",
            AssembledText = "TEXT 0",
            Status = ContractStatus.Generated,
        };
        _dbContext.Contracts.Add(contract);
        await _dbContext.SaveChangesAsync();

        // Simulate a long-lived contract already at the cap.
        var baseTime = DateTime.UtcNow.AddHours(-5);
        for (var i = 1; i <= MaxChatMessagesPerContract; i++)
        {
            _dbContext.ChatMessages.Add(new ChatMessage
            {
                ContractId = contract.Id,
                Role = i % 2 == 0 ? MessageRole.Assistant : MessageRole.User,
                Content = $"Message {i}",
                CreatedAt = baseTime.AddSeconds(i),
            });
        }

        await _dbContext.SaveChangesAsync();

        _fakeAi.EditResult = new ContractEditResult { Success = true, UpdatedContractText = "TEXT 1" };
        await _service.ApplyChatEditAsync(contract.Id, "One more edit");

        // 200 pre-existing plus the two appended by this edit; the two oldest are pruned,
        // so the stored history settles exactly at the cap.
        Assert.Equal(MaxChatMessagesPerContract, await _dbContext.ChatMessages.CountAsync());

        var remaining = await _dbContext.ChatMessages
            .AsNoTracking()
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync();

        Assert.Equal("One more edit", remaining[^2].Content);
        Assert.Equal(ContractService.ChatEditConfirmation, remaining[^1].Content);
        Assert.DoesNotContain(remaining, m => m.Content == "Message 1");
    }

    /// <summary>Minimal in-memory AI service returning a scripted edit result.</summary>
    private sealed class FakeAi : IContractAiService
    {
        public ContractEditResult EditResult { get; set; } =
            new() { Success = true, UpdatedContractText = "UPDATED" };

        public Task<string> GenerateClauseBodyAsync(Clause clause, Dictionary<string, string> fieldValues)
            => Task.FromResult("Filled clause body.");

        public Task<string> AssembleContractAsync(
            Contract contract,
            List<ContractClause> clauses,
            Dictionary<string, string> fieldValues)
            => Task.FromResult("ASSEMBLED");

        public Task<string> AssembleContractAsync(
            Contract contract,
            List<(ContractClause ContractClause, Clause Clause)> clauses,
            Dictionary<string, string> fieldValues)
            => Task.FromResult("ASSEMBLED");

        public Task<ContractEditResult> ApplyChatEditAsync(Contract contract, string currentDocument, string userInstruction)
            => Task.FromResult(EditResult);

        public Task<bool> TestConnectionAsync() => Task.FromResult(true);

        public Task<OpenAiConnectionTestResult> TestConnectionDetailedAsync()
            => Task.FromResult(new OpenAiConnectionTestResult { Success = true, Message = "OK" });
    }
}
