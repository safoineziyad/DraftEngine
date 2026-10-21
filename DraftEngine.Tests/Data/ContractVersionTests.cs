using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for the contract version history implemented by <see cref="ContractService"/>
/// (audit Findings #1/#16): snapshots taken automatically before AI chat edits,
/// regenerations and reverts, per-contract numbering, pruning and cascade deletion.
/// A fake <see cref="IContractAiService"/> guarantees the OpenAI API is never contacted.
/// </summary>
public class ContractVersionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DraftEngineDbContext _dbContext;
    private readonly TestDatabaseContextFactory _databaseFactory;
    private readonly FakeAi _fakeAi = new();
    private readonly ContractService _service;
    private readonly FieldService _fieldService;

    public ContractVersionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(options);
        _dbContext.Database.EnsureCreated();

        _databaseFactory = new TestDatabaseContextFactory(options);
        _fieldService = new FieldService(_databaseFactory);
        _service = new ContractService(_databaseFactory, _fieldService, _fakeAi);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------- snapshots on edit

    [Fact]
    public async Task ApplyChatEditAsync_SnapshotsThePreEditText()
    {
        var contract = await CreateGeneratedContractAsync("ORIGINAL TEXT");
        _fakeAi.EditResult = new ContractEditResult { Success = true, UpdatedContractText = "EDITED TEXT" };

        await _service.ApplyChatEditAsync(contract.Id, "Tighten the term");

        var version = Assert.Single(await _service.GetVersionsAsync(contract.Id));
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(ContractService.AiEditReason, version.Reason);
        Assert.Equal("ORIGINAL TEXT", version.Text);
    }

    [Fact]
    public async Task ApplyChatEditAsync_SequentialEdits_IncrementVersionNumbers()
    {
        var contract = await CreateGeneratedContractAsync("TEXT 0");

        _fakeAi.EditResult = new ContractEditResult { Success = true, UpdatedContractText = "TEXT 1" };
        await _service.ApplyChatEditAsync(contract.Id, "First edit");

        _fakeAi.EditResult = new ContractEditResult { Success = true, UpdatedContractText = "TEXT 2" };
        await _service.ApplyChatEditAsync(contract.Id, "Second edit");

        var versions = await _service.GetVersionsAsync(contract.Id);
        Assert.Equal(2, versions.Count);
        Assert.Equal(new[] { 1, 2 }, versions.Select(v => v.VersionNumber));
        Assert.Equal(new[] { "TEXT 0", "TEXT 1" }, versions.Select(v => v.Text));
    }

    [Fact]
    public async Task ApplyChatEditAsync_FailedEdit_CreatesNoSnapshot()
    {
        var contract = await CreateGeneratedContractAsync("ORIGINAL TEXT");
        _fakeAi.EditResult = new ContractEditResult { Success = false, ErrorMessage = "Try again." };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ApplyChatEditAsync(contract.Id, "Broken edit"));

        Assert.Empty(await _service.GetVersionsAsync(contract.Id));
    }

    // -------------------------------------------------- snapshots on generation

    [Fact]
    public async Task GenerateContractAsync_FirstGeneration_CreatesNoSnapshot()
    {
        var contract = await CreateContractAsync();
        await AttachClausesAsync(contract.Id, 1);

        await _service.GenerateContractAsync(contract.Id, new List<int>());

        Assert.Empty(await _service.GetVersionsAsync(contract.Id));
    }

    [Fact]
    public async Task GenerateContractAsync_Regeneration_SnapshotsThePreviousText()
    {
        var contract = await CreateGeneratedContractAsync("PREVIOUSLY GENERATED");

        await _service.GenerateContractAsync(contract.Id, new List<int>());

        var version = Assert.Single(await _service.GetVersionsAsync(contract.Id));
        Assert.Equal(ContractService.RegenerationReason, version.Reason);
        Assert.Equal("PREVIOUSLY GENERATED", version.Text);
    }

    // ------------------------------------------------------------------ revert

    [Fact]
    public async Task RevertToVersionAsync_RestoresText_AndSnapshotsTheReplacedText()
    {
        var contract = await CreateGeneratedContractAsync("TEXT 0");
        _fakeAi.EditResult = new ContractEditResult { Success = true, UpdatedContractText = "TEXT 1" };
        await _service.ApplyChatEditAsync(contract.Id, "First edit");
        _fakeAi.EditResult = new ContractEditResult { Success = true, UpdatedContractText = "TEXT 2" };
        await _service.ApplyChatEditAsync(contract.Id, "Second edit");

        var firstVersion = (await _service.GetVersionsAsync(contract.Id))[0];
        var restored = await _service.RevertToVersionAsync(contract.Id, firstVersion.Id);

        Assert.Equal("TEXT 0", restored);

        var stored = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal("TEXT 0", stored.AssembledText);

        // The text replaced by the revert ("TEXT 2") is itself snapshotted: reverting is undoable.
        var versions = await _service.GetVersionsAsync(contract.Id);
        Assert.Equal(3, versions.Count);
        Assert.Equal(ContractService.RevertReason, versions[^1].Reason);
        Assert.Equal("TEXT 2", versions[^1].Text);
        Assert.Equal(3, versions[^1].VersionNumber);
    }

    [Fact]
    public async Task RevertToVersionAsync_UnknownVersion_ThrowsFriendlyMessage()
    {
        var contract = await CreateGeneratedContractAsync("TEXT");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RevertToVersionAsync(contract.Id, versionId: 9999));

        Assert.Contains("could not be found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevertToVersionAsync_AlreadyCurrentText_ExplainsAndDoesNothing()
    {
        var contract = await CreateGeneratedContractAsync("TEXT 0");
        _fakeAi.EditResult = new ContractEditResult { Success = true, UpdatedContractText = "TEXT 1" };
        await _service.ApplyChatEditAsync(contract.Id, "Edit");

        var firstVersion = (await _service.GetVersionsAsync(contract.Id))[0]; // text "TEXT 0"
        await _service.RevertToVersionAsync(contract.Id, firstVersion.Id);    // now showing "TEXT 0"
        var versionsAfterFirstRevert = (await _service.GetVersionsAsync(contract.Id)).Count;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RevertToVersionAsync(contract.Id, firstVersion.Id));

        Assert.Contains("already shows this version", exception.Message, StringComparison.Ordinal);
        Assert.Equal(versionsAfterFirstRevert, (await _service.GetVersionsAsync(contract.Id)).Count);
    }

    // -------------------------------------------------------- pruning + delete

    [Fact]
    public async Task ApplyChatEditAsync_BeyondTheCap_PrunesTheOldestSnapshots()
    {
        var contract = await CreateGeneratedContractAsync("TEXT 0");

        // Simulate a long-lived contract that already hit the 50-snapshot cap.
        for (var i = 1; i <= 50; i++)
        {
            _dbContext.ContractVersions.Add(new ContractVersion
            {
                ContractId = contract.Id,
                VersionNumber = i,
                Text = $"TEXT {i}",
                Reason = ContractService.AiEditReason,
            });
        }

        await _dbContext.SaveChangesAsync();

        _fakeAi.EditResult = new ContractEditResult { Success = true, UpdatedContractText = "TEXT 51" };
        await _service.ApplyChatEditAsync(contract.Id, "One more edit");

        var versions = await _service.GetVersionsAsync(contract.Id);
        Assert.Equal(50, versions.Count);
        Assert.Equal(2, versions[0].VersionNumber); // snapshot #1 was pruned; #51 is the newest.
        Assert.Equal(51, versions[^1].VersionNumber);
        Assert.Equal("TEXT 0", versions[^1].Text);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheVersionHistory()
    {
        var contract = await CreateGeneratedContractAsync("TEXT 0");
        _fakeAi.EditResult = new ContractEditResult { Success = true, UpdatedContractText = "TEXT 1" };
        await _service.ApplyChatEditAsync(contract.Id, "Edit");
        Assert.NotEmpty(await _service.GetVersionsAsync(contract.Id));

        await _service.DeleteAsync(contract.Id);

        Assert.Empty(await _dbContext.ContractVersions.AsNoTracking().ToListAsync());
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Creates a contract with the given assembled text (no generation involved).</summary>
    private async Task<Contract> CreateGeneratedContractAsync(string assembledText)
    {
        var contract = await CreateContractAsync();
        contract.AssembledText = assembledText;
        contract.Status = ContractStatus.Generated;
        await _dbContext.SaveChangesAsync();
        return contract;
    }

    private async Task<Contract> CreateContractAsync()
    {
        var contract = new Contract
        {
            User = new User { Email = "dev@example.com", DisplayName = "Dev" },
            Title = "Test NDA",
            ContractType = "Mutual NDA",
            ClientName = "Acme Inc",
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
                Selected = true,
            });
        }

        await _dbContext.SaveChangesAsync();
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
