using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for the AI-edit response validation in <see cref="ContractService"/>
/// (audit Finding #2): a reply that truncates the document or silently drops clauses must be
/// rejected with a friendly message, leaving the stored contract untouched. The OpenAI API is
/// never contacted — edits come from a scripted fake service.
/// </summary>
public class ContractAiEditValidationTests : IDisposable
{
    private const string FiveClauseDocument =
        "Test NDA\n\n" +
        "1. Confidentiality\nThe parties agree to keep all information confidential.\n\n" +
        "2. Exclusions\nConfidential information does not include public information.\n\n" +
        "3. Term\nThis Agreement lasts twelve months.\n\n" +
        "4. Governing Law\nThis Agreement is governed by the laws of England.\n\n" +
        "5. Signatures\nSigned by both parties on the date above.\n";

    private readonly SqliteConnection _connection;
    private readonly DraftEngineDbContext _dbContext;
    private readonly TestDatabaseContextFactory _databaseFactory;
    private readonly FakeAi _fakeAi = new();
    private readonly ContractService _service;

    public ContractAiEditValidationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(options);
        _dbContext.Database.EnsureCreated();

        _databaseFactory = new TestDatabaseContextFactory(options);
        _service = new ContractService(_databaseFactory, new FieldService(_databaseFactory), _fakeAi);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ApplyChatEditAsync_TruncatedReply_IsRejectedAndContractUnchanged()
    {
        var contract = await CreateGeneratedContractAsync(FiveClauseDocument);
        _fakeAi.EditResult = new ContractEditResult
        {
            Success = true,
            UpdatedContractText = "1. Confidentiality\nThe parties agree to keep all information confidential.",
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ApplyChatEditAsync(contract.Id, "Tighten the term"));

        Assert.Contains("looks incomplete", exception.Message, StringComparison.Ordinal);

        var stored = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(FiveClauseDocument, stored.AssembledText);

        // The instruction is still recorded, matching the other failure paths.
        var messages = await _dbContext.ChatMessages.AsNoTracking().ToListAsync();
        Assert.Equal("Tighten the term", Assert.Single(messages).Content);

        // Nothing was replaced, so no version snapshot was needed either.
        Assert.Empty(await _dbContext.ContractVersions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ApplyChatEditAsync_ReplyDroppingMultipleClauses_IsRejected()
    {
        var contract = await CreateGeneratedContractAsync(FiveClauseDocument);
        _fakeAi.EditResult = new ContractEditResult
        {
            Success = true,
            // Still long enough to pass the length floor, but three clauses vanished.
            UpdatedContractText =
                "Test NDA\n\n1. Confidentiality\n" +
                "The parties agree to keep all information confidential for the full term of this " +
                "Agreement and beyond, subject only to the exclusions set out in this document and " +
                "to any disclosures required by law, which the parties will handle responsibly and " +
                "with due care for each other's legitimate business interests at all times.\n",
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ApplyChatEditAsync(contract.Id, "Condense the document"));

        Assert.Contains("removed clauses", exception.Message, StringComparison.Ordinal);

        var stored = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(FiveClauseDocument, stored.AssembledText);
    }

    [Fact]
    public async Task ApplyChatEditAsync_ReplyCondensingASingleClause_IsAccepted()
    {
        var contract = await CreateGeneratedContractAsync(FiveClauseDocument);
        _fakeAi.EditResult = new ContractEditResult
        {
            Success = true,
            UpdatedContractText = FiveClauseDocument.Replace(
                "This Agreement is governed by the laws of England.",
                "This Agreement is governed by the laws of England and Wales."),
        };

        var updated = await _service.ApplyChatEditAsync(contract.Id, "Name England and Wales");

        Assert.Contains("England and Wales", updated, StringComparison.Ordinal);

        // The accepted edit is versioned like any other chat edit.
        var version = Assert.Single(await _service.GetVersionsAsync(contract.Id));
        Assert.Equal(FiveClauseDocument, version.Text);
    }

    [Fact]
    public async Task ApplyChatEditAsync_ShorterButCompleteReply_IsAccepted()
    {
        var contract = await CreateGeneratedContractAsync(FiveClauseDocument);
        var compact = FiveClauseDocument.Replace(
            "The parties agree to keep all information confidential.",
            "The parties will keep all information confidential.");

        _fakeAi.EditResult = new ContractEditResult { Success = true, UpdatedContractText = compact };

        var updated = await _service.ApplyChatEditAsync(contract.Id, "Shorten the first clause");

        Assert.Contains("The parties will keep", updated, StringComparison.Ordinal);
    }

    /// <summary>Creates a contract carrying the given assembled text (no generation involved).</summary>
    private async Task<Contract> CreateGeneratedContractAsync(string assembledText)
    {
        var contract = new Contract
        {
            User = new User { Email = "dev@example.com", DisplayName = "Dev" },
            Title = "Test NDA",
            ContractType = "Mutual NDA",
            ClientName = "Acme Inc",
            AssembledText = assembledText,
            Status = ContractStatus.Generated,
        };

        _dbContext.Contracts.Add(contract);
        await _dbContext.SaveChangesAsync();
        return contract;
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
