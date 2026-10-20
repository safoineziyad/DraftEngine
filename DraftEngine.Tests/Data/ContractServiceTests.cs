using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for <see cref="ContractService"/> (CRUD plus GenerateContractAsync /
/// ApplyChatEditAsync orchestration) using a fake <see cref="IContractAiService"/>
/// so the OpenAI API is never contacted.
/// </summary>
public class ContractServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DraftEngineDbContext _dbContext;
    private readonly TestDatabaseContextFactory _databaseFactory;
    private readonly FakeContractAiService _fakeAi = new();
    private readonly ContractService _service;
    private readonly FieldService _fieldService;
    private readonly ChatService _chatService;

    public ContractServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(options);
        _dbContext.Database.EnsureCreated();

        // Same pattern as production: services open one short-lived context per operation.
        _databaseFactory = new TestDatabaseContextFactory(options);
        _fieldService = new FieldService(_databaseFactory);
        _chatService = new ChatService(_databaseFactory);
        _service = new ContractService(_databaseFactory, _fieldService, _fakeAi);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------- CRUD

    [Fact]
    public async Task CreateAsync_SetsTimestamps_AndPersists()
    {
        var contract = await _service.CreateAsync(new Contract
        {
            User = new User
            {
                Email = "user@example.com",
                DisplayName = "Test User",
                OpenAiApiKeyEncrypted = string.Empty
            },
            Title = "Test NDA",
            ContractType = "Mutual NDA",
            ClientName = "Acme Inc",
            ClientEmail = "legal@acme.example",
            EffectiveDate = new DateTime(2026, 1, 15),
            GoverningLaw = "Delaware",
            TermMonths = 12
        });

        Assert.True(contract.Id > 0);
        Assert.Equal(DateTime.UtcNow.Year, contract.CreatedAt.Year);
        // CreatedAt/UpdatedAt are stamped in sequence, so allow a tiny clock delta.
        Assert.True(contract.UpdatedAt >= contract.CreatedAt);
        Assert.True((contract.UpdatedAt - contract.CreatedAt).TotalMilliseconds < 1000);

        var persisted = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal("Test NDA", persisted.Title);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsContractWithClausesAndFields()
    {
        var contract = await SeedContractAsync();
        await AttachClausesAsync(contract.Id, 1, 2);

        var loaded = await _service.GetByIdAsync(contract.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Test NDA", loaded!.Title);
        Assert.Equal(2, loaded.ContractClauses.Count);
        Assert.All(loaded.ContractClauses, cc => Assert.NotNull(cc.Clause));
    }

    [Fact]
    public async Task GetAllAsync_FiltersByUser_NewestFirst()
    {
        var contractA = await SeedContractAsync(email: "a@example.com");
        await Task.Delay(10);
        var contractB = await SeedContractAsync(email: "a@example.com");
        await SeedContractAsync(email: "other@example.com");

        var mine = await _service.GetAllAsync(contractA.UserId);

        Assert.Equal(2, mine.Count);
        Assert.Equal(contractB.Id, mine[0].Id);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesScalars_AndBumpsUpdatedAt()
    {
        var contract = await SeedContractAsync();
        var before = contract.UpdatedAt;
        await Task.Delay(10);

        contract.Title = "Renamed NDA";
        var updated = await _service.UpdateAsync(contract);

        Assert.NotNull(updated);
        Assert.Equal("Renamed NDA", updated!.Title);
        Assert.True(updated.UpdatedAt > before);
    }

    [Fact]
    public async Task DeleteAsync_RemovesContract_AndIsIdempotent()
    {
        var contract = await SeedContractAsync();
        await AttachClausesAsync(contract.Id, 1);

        await _service.DeleteAsync(contract.Id);
        await _service.DeleteAsync(contract.Id); // second delete is a no-op

        Assert.Null(await _service.GetByIdAsync(contract.Id));
        Assert.Empty(await _dbContext.ContractClauses.Where(cc => cc.ContractId == contract.Id).ToListAsync());
    }

    // ---------------------------------------------------------------- GenerateContractAsync

    [Fact]
    public async Task GenerateContractAsync_FillsTemplates_PersistsAssembledText()
    {
        var contract = await SeedContractAsync();
        await AttachClausesAsync(contract.Id, 4, 5); // Term ({{TermMonths}}, {{EffectiveDate}}) + Governing Law

        var result = await _service.GenerateContractAsync(contract.Id, new List<int> { 4, 5 });

        // Deterministic template assembly: sequential headings and filled placeholders.
        Assert.Contains("1. Term", result);
        Assert.Contains("12 months from the Effective Date (January 15, 2026)", result);
        Assert.Contains("2. Governing Law", result);
        Assert.Contains("the laws of Delaware", result);
        Assert.DoesNotContain("{{", result);

        var stored = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(result, stored.AssembledText);
        Assert.Equal(ContractStatus.Generated, stored.Status);
        Assert.NotNull(stored.LastGeneratedAt);
    }

    [Fact]
    public async Task GenerateContractAsync_UseAiPolish_UsesAiText()
    {
        var contract = await SeedContractAsync();
        await AttachClausesAsync(contract.Id, 4);
        _fakeAi.AssembledText = "ASSEMBLED CONTRACT TEXT";

        var result = await _service.GenerateContractAsync(contract.Id, new List<int> { 4 }, useAiPolish: true);

        Assert.Equal("ASSEMBLED CONTRACT TEXT", result);

        var stored = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal("ASSEMBLED CONTRACT TEXT", stored.AssembledText);

        // Assembly received exactly the one selected clause.
        Assert.NotNull(_fakeAi.LastAssemblyClauses);
        Assert.Single(_fakeAi.LastAssemblyClauses!);
        Assert.Equal(4, _fakeAi.LastAssemblyClauses![0].ContractClause.ClauseId);
    }

    [Fact]
    public async Task GenerateContractAsync_AiPolishFails_FallsBackToTemplateText()
    {
        var contract = await SeedContractAsync();
        await AttachClausesAsync(contract.Id, 5);
        _fakeAi.AssemblyException = new HttpRequestException("network down");

        var result = await _service.GenerateContractAsync(contract.Id, new List<int> { 5 }, useAiPolish: true);

        // A failing AI must not break generation: the deterministic template text is used.
        Assert.Contains("the laws of Delaware", result);
        Assert.DoesNotContain("{{", result);
    }

    [Fact]
    public async Task GenerateContractAsync_SyncsSelection_WithRequestedClausesOnly()
    {
        var contract = await SeedContractAsync();
        await AttachClausesAsync(contract.Id, 1, 2, 3);

        await _service.GenerateContractAsync(contract.Id, new List<int> { 2 }, useAiPolish: true);

        var selections = await _dbContext.ContractClauses
            .AsNoTracking()
            .Where(cc => cc.ContractId == contract.Id)
            .OrderBy(cc => cc.ClauseId)
            .ToListAsync();
        Assert.False(selections.Single(cc => cc.ClauseId == 1).Selected);
        Assert.True(selections.Single(cc => cc.ClauseId == 2).Selected);
        Assert.False(selections.Single(cc => cc.ClauseId == 3).Selected);

        // Assembly received exactly the one selected clause.
        Assert.NotNull(_fakeAi.LastAssemblyClauses);
        Assert.Single(_fakeAi.LastAssemblyClauses!);
        Assert.Equal(2, _fakeAi.LastAssemblyClauses![0].ContractClause.ClauseId);
    }

    [Fact]
    public async Task GenerateContractAsync_EmptySelectionList_KeepsStoredSelection()
    {
        var contract = await SeedContractAsync();
        await AttachClausesAsync(contract.Id, 1, 2);

        var result = await _service.GenerateContractAsync(contract.Id, new List<int>());

        // An empty list does not touch the stored selection: both clauses are still included.
        Assert.Contains("1. Confidentiality", result);
        Assert.Contains("2. Definition of Confidential Information", result);
    }

    [Fact]
    public async Task GenerateContractAsync_UnknownContract_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GenerateContractAsync(4242, new List<int> { 1 }));
    }

    // ---------------------------------------------------------------- ApplyChatEditAsync

    [Fact]
    public async Task ApplyChatEditAsync_PersistsNewText_AndRecordsTranscript()
    {
        var contract = await SeedContractAsync(assembledText: "OLD TEXT");
        _fakeAi.EditResult = new ContractEditResult { Success = true, UpdatedContractText = "NEW TEXT" };

        var result = await _service.ApplyChatEditAsync(contract.Id, "Add a termination clause");

        Assert.Equal("NEW TEXT", result);

        var stored = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal("NEW TEXT", stored.AssembledText);

        var messages = await _chatService.GetMessagesAsync(contract.Id);
        Assert.Equal(2, messages.Count);
        Assert.Equal(MessageRole.User, messages[0].Role);
        Assert.Equal("Add a termination clause", messages[0].Content);
        Assert.Equal(MessageRole.Assistant, messages[1].Role);
        Assert.Equal(ContractService.ChatEditConfirmation, messages[1].Content);
    }

    [Fact]
    public async Task ApplyChatEditAsync_AiFailure_ThrowsUserSafeMessage_AndKeepsText()
    {
        var contract = await SeedContractAsync(assembledText: "OLD TEXT");
        _fakeAi.EditResult = new ContractEditResult { Success = false, ErrorMessage = "Your OpenAI account has no available credits." };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApplyChatEditAsync(contract.Id, "Rewrite the term"));

        Assert.Equal("Your OpenAI account has no available credits.", ex.Message);

        var stored = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal("OLD TEXT", stored.AssembledText);

        // The failed attempt still records the user's instruction, but no assistant reply.
        var messages = await _chatService.GetMessagesAsync(contract.Id);
        Assert.Single(messages);
        Assert.Equal(MessageRole.User, messages[0].Role);
        Assert.Equal("Rewrite the term", messages[0].Content);
    }

    [Fact]
    public async Task ApplyChatEditAsync_BeforeGeneration_Throws()
    {
        var contract = await SeedContractAsync(assembledText: string.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApplyChatEditAsync(contract.Id, "Anything"));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Seeds a user + contract for the given email (reusing the user when it already exists).</summary>
    private async Task<Contract> SeedContractAsync(
        string email = "user@example.com",
        string assembledText = "")
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            user = new User
            {
                Email = email,
                DisplayName = "Test User",
                OpenAiApiKeyEncrypted = string.Empty
            };
            _dbContext.Users.Add(user);
        }

        var contract = new Contract
        {
            User = user,
            Title = "Test NDA",
            ContractType = "Mutual NDA",
            ClientName = "Acme Inc",
            ClientEmail = "legal@acme.example",
            EffectiveDate = new DateTime(2026, 1, 15),
            GoverningLaw = "Delaware",
            TermMonths = 12,
            AssembledText = assembledText
        };

        _dbContext.Contracts.Add(contract);
        await _dbContext.SaveChangesAsync();
        return contract;
    }

    /// <summary>Attaches seeded clause templates (by id) to a contract as selected rows.</summary>
    private async Task AttachClausesAsync(int contractId, params int[] clauseIds)
    {
        var clauses = await _dbContext.Clauses
            .Where(c => clauseIds.Contains(c.Id))
            .ToListAsync();

        foreach (var clause in clauses)
        {
            _dbContext.ContractClauses.Add(new ContractClause
            {
                ContractId = contractId,
                ClauseId = clause.Id,
                Selected = true
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    /// <summary>Minimal in-memory <see cref="IContractAiService"/> recording what it received.</summary>
    private sealed class FakeContractAiService : IContractAiService
    {
        public string AssembledText { get; set; } = "ASSEMBLED";
        public ContractEditResult EditResult { get; set; } = new() { Success = true, UpdatedContractText = "UPDATED" };
        public Exception? AssemblyException { get; set; }
        public List<(ContractClause ContractClause, Clause Clause)>? LastAssemblyClauses { get; private set; }

        public Task<string> GenerateClauseBodyAsync(Clause clause, Dictionary<string, string> fieldValues)
            => Task.FromResult("Filled clause body.");

        public Task<string> AssembleContractAsync(Contract contract, List<ContractClause> clauses, Dictionary<string, string> fieldValues)
            => Task.FromResult(AssembledText);

        public Task<string> AssembleContractAsync(
            Contract contract,
            List<(ContractClause ContractClause, Clause Clause)> clauses,
            Dictionary<string, string> fieldValues)
        {
            if (AssemblyException is not null)
            {
                throw AssemblyException;
            }

            LastAssemblyClauses = clauses;
            return Task.FromResult(AssembledText);
        }

        public Task<ContractEditResult> ApplyChatEditAsync(Contract contract, string currentDocument, string userInstruction)
            => Task.FromResult(EditResult);

        public Task<bool> TestConnectionAsync() => Task.FromResult(true);

        public Task<OpenAiConnectionTestResult> TestConnectionDetailedAsync()
            => Task.FromResult(new OpenAiConnectionTestResult { Success = true, Message = "OK" });
    }
}
