using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for the preview/accept AI edit flow and the party-data redaction applied before
/// any AI request. The AI provider is a scripted fake; nothing leaves the process.
/// </summary>
public class AiEditPreviewTests : IDisposable
{
    private const string Document =
        "Mutual Non-Disclosure Agreement\n\n" +
        "1. Confidentiality\nAcme Secret Inc and Beta Holdings LLC agree to keep information confidential.\n\n" +
        "2. Term\nThis Agreement lasts twelve months.\n";

    private readonly SqliteConnection _connection;
    private readonly DraftEngineDbContext _dbContext;
    private readonly TestDatabaseContextFactory _databaseFactory;
    private readonly CapturingAi _fakeAi = new();
    private readonly AiRedactionService _redaction = new() { IsEnabled = true };
    private readonly ContractService _service;

    public AiEditPreviewTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(options);
        _dbContext.Database.EnsureCreated();

        _databaseFactory = new TestDatabaseContextFactory(options);
        _service = new ContractService(
            _databaseFactory,
            new FieldService(_databaseFactory),
            _fakeAi,
            redaction: _redaction);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Preview_DoesNotChangeTheStoredContract()
    {
        var contract = await CreateContractAsync();

        var preview = await _service.PreviewChatEditAsync(contract.Id, "Add a sentence about notices.");

        Assert.Equal(Document, preview.OriginalText);
        Assert.Contains("3. Notices", preview.UpdatedText, StringComparison.Ordinal);
        Assert.True(ContractTextDiff.HasChanges(preview.OriginalText, preview.UpdatedText));

        var stored = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(Document, stored.AssembledText);
        Assert.Empty(await _dbContext.ContractVersions.AsNoTracking().ToListAsync());
        Assert.Empty(await _dbContext.ChatMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Preview_RedactsPartyNamesBeforeTheRequestAndRestoresThemInTheReply()
    {
        var contract = await CreateContractAsync();

        var preview = await _service.PreviewChatEditAsync(
            contract.Id,
            "Ask Acme Secret Inc to confirm the address.");

        // The provider never saw a real party name or address.
        Assert.DoesNotContain("Acme Secret Inc", _fakeAi.LastDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("Beta Holdings LLC", _fakeAi.LastDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme Secret Inc", _fakeAi.LastInstruction, StringComparison.Ordinal);
        Assert.DoesNotContain("1 Acme Way", _fakeAi.LastDocument, StringComparison.Ordinal);

        // The user-facing preview has the real values back.
        Assert.Contains("Acme Secret Inc", preview.UpdatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accept_SnapshotsTheOldTextAndStoresTheEdit()
    {
        var contract = await CreateContractAsync();
        var preview = await _service.PreviewChatEditAsync(contract.Id, "Add a notices sentence.");

        var updated = await _service.ApplyChatEditPreviewAsync(
            contract.Id,
            "Add a notices sentence.",
            preview.UpdatedText);

        Assert.Equal(preview.UpdatedText, updated);

        var stored = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(preview.UpdatedText, stored.AssembledText);

        var version = Assert.Single(await _dbContext.ContractVersions.AsNoTracking().ToListAsync());
        Assert.Equal(Document, version.Text);
        Assert.Equal(ContractService.AiEditReason, version.Reason);

        var messages = await _dbContext.ChatMessages.AsNoTracking().ToListAsync();
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task Accept_RejectsAStaleTruncatedReplacement()
    {
        var contract = await CreateContractAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ApplyChatEditPreviewAsync(contract.Id, "Condense", "too short"));

        var stored = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(Document, stored.AssembledText);
    }

    private async Task<Contract> CreateContractAsync()
    {
        var contract = new Contract
        {
            User = new User { Email = "dev@example.com", DisplayName = "Dev" },
            Title = "Acme NDA",
            ContractType = "Mutual NDA",
            ClientName = "Beta Holdings LLC",
            PartiesJson =
                "[{\"name\":\"Acme Secret Inc\",\"address\":\"1 Acme Way\"}," +
                "{\"name\":\"Beta Holdings LLC\",\"address\":\"9 Beta Road\"}]",
            AssembledText = Document,
            Status = ContractStatus.Generated,
        };

        _dbContext.Contracts.Add(contract);
        await _dbContext.SaveChangesAsync();
        return contract;
    }

    /// <summary>Scripted AI that captures exactly what the provider would have received.</summary>
    private sealed class CapturingAi : IContractAiService
    {
        public string LastDocument { get; private set; } = string.Empty;
        public string LastInstruction { get; private set; } = string.Empty;

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

        public Task<ContractEditResult> ApplyChatEditAsync(
            Contract contract,
            string currentDocument,
            string userInstruction)
        {
            LastDocument = currentDocument;
            LastInstruction = userInstruction;
            return Task.FromResult(new ContractEditResult
            {
                Success = true,
                UpdatedContractText = currentDocument + "\n3. Notices\nNotices must be in writing.\n",
            });
        }

        public Task<bool> TestConnectionAsync() => Task.FromResult(true);

        public Task<OpenAiConnectionTestResult> TestConnectionDetailedAsync()
            => Task.FromResult(new OpenAiConnectionTestResult { Success = true, Message = "OK" });
    }
}
