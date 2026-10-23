using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Regression tests for the history pruning that runs after a change was committed.
/// <para>
/// The version and chat history are pruned by their own <c>SaveChanges</c> calls, which run
/// <em>after</em> the change itself was committed. A failure there used to escape as a
/// failed edit: the user was told the assistant could not apply the edit although the new
/// text was already stored, the editor kept showing the old text, and the exported document
/// was built from that stale text.
/// </para>
/// </summary>
public class ContractPruneFailureTests : IDisposable
{
    private const string OriginalText = """
        1. Confidentiality
        Each party shall keep confidential all information disclosed by the other party and shall not disclose it to any third party without the prior written consent of the disclosing party.

        2. Term
        This Agreement commences on the Effective Date and continues for twelve (12) months unless terminated earlier by either party upon thirty (30) days prior written notice.
        """;

    private const string EditedText = """
        1. Confidentiality
        Each party shall keep confidential all information disclosed by the other party and shall not disclose it to any third party without the prior written consent of the disclosing party for three years.

        2. Term
        This Agreement commences on the Effective Date and continues for twelve (12) months unless terminated earlier by either party upon thirty (30) days prior written notice.
        """;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DraftEngineDbContext> _options;
    private readonly SaveCounter _counter = new();

    public ContractPruneFailureTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DraftEngineDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A failing history prune must not turn a committed chat edit into a failed one.
    /// </summary>
    [Fact]
    public async Task ApplyChatEditAsync_WhenHistoryPruningFails_StillAppliesTheEdit()
    {
        var contractId = await SeedContractWithOverflowingHistoryAsync();

        // Save 1 commits the edit; save 2 is the history prune and is made to fail.
        _counter.FailFromSaveNumber = 2;

        var factory = new FaultInjectingDatabaseContextFactory(_options, _counter);
        var service = new ContractService(factory, new FieldService(factory), new FakeAiService());

        var result = await service.ApplyChatEditAsync(contractId, "Make the confidentiality term three years.");

        Assert.Contains("three years", result);

        // The edit was committed even though the cleanup after it failed.
        using var context = new DraftEngineDbContext(_options);
        var stored = await context.Contracts.AsNoTracking().SingleAsync(c => c.Id == contractId);
        Assert.Equal(result, stored.AssembledText);
    }

    /// <summary>
    /// A failing history prune must not turn a committed regeneration into a failed one.
    /// </summary>
    [Fact]
    public async Task GenerateContractAsync_WhenHistoryPruningFails_StillStoresTheText()
    {
        var contractId = await SeedContractWithOverflowingHistoryAsync();

        // Save 1 syncs the clause selection, save 2 commits the regenerated text and
        // save 3 is the history prune, which is made to fail.
        _counter.FailFromSaveNumber = 3;

        var factory = new FaultInjectingDatabaseContextFactory(_options, _counter);
        var service = new ContractService(factory, new FieldService(factory), new FakeAiService());

        var text = await service.GenerateContractAsync(contractId, new List<int> { 4 });

        Assert.Contains("12 months", text);

        using var context = new DraftEngineDbContext(_options);
        var stored = await context.Contracts.AsNoTracking().SingleAsync(c => c.Id == contractId);
        Assert.Equal(text, stored.AssembledText);
    }

    private async Task<int> SeedContractWithOverflowingHistoryAsync()
    {
        using var context = new DraftEngineDbContext(_options);

        var contract = new Contract
        {
            UserId = 1,
            Title = "Mutual Non-Disclosure Agreement",
            ContractType = "Mutual NDA",
            ClientName = "Acme Corp",
            ClientEmail = "legal@acme.example",
            GoverningLaw = "England and Wales",
            TermMonths = 12,
            PartiesJson = "[]",
            AssembledText = OriginalText
        };

        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        // More snapshots than the history cap, so the prune has rows to delete and
        // therefore performs its own save after the change was committed.
        for (var version = 1; version <= 55; version++)
        {
            context.ContractVersions.Add(new ContractVersion
            {
                ContractId = contract.Id,
                VersionNumber = version,
                Text = OriginalText,
                Reason = "Test",
                CreatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        return contract.Id;
    }

    /// <summary>Counts the saves performed across every context of one operation.</summary>
    private sealed class SaveCounter
    {
        private int _saveCount;

        /// <summary>First save number that fails; defaults to "never fail".</summary>
        public int FailFromSaveNumber { get; set; } = int.MaxValue;

        public int Next()
        {
            _saveCount++;
            return _saveCount;
        }
    }

    /// <summary>
    /// Hands out contexts whose saves fail once the counter reaches the configured number.
    /// </summary>
    private sealed class FaultInjectingDatabaseContextFactory : IDatabaseContextFactory
    {
        private readonly DbContextOptions<DraftEngineDbContext> _options;
        private readonly SaveCounter _counter;

        public FaultInjectingDatabaseContextFactory(DbContextOptions<DraftEngineDbContext> options, SaveCounter counter)
        {
            _options = options;
            _counter = counter;
        }

        public DbContext CreateContext() => new FailingSaveContext(_options, _counter);
    }

    private sealed class FailingSaveContext : DraftEngineDbContext
    {
        private readonly SaveCounter _counter;

        public FailingSaveContext(DbContextOptions<DraftEngineDbContext> options, SaveCounter counter)
            : base(options)
        {
            _counter = counter;
        }

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            if (_counter.Next() >= _counter.FailFromSaveNumber)
            {
                throw new DbUpdateException("Injected save failure.");
            }

            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    private sealed class FakeAiService : IContractAiService
    {
        public Task<ContractEditResult> ApplyChatEditAsync(
            Contract contract,
            string currentDocument,
            string userInstruction) =>
            Task.FromResult(new ContractEditResult
            {
                Success = true,
                UpdatedContractText = EditedText
            });

        public Task<string> GenerateClauseBodyAsync(Clause clause, Dictionary<string, string> fieldValues) =>
            throw new NotSupportedException();

        public Task<string> AssembleContractAsync(
            Contract contract,
            List<ContractClause> clauses,
            Dictionary<string, string> fieldValues) =>
            throw new NotSupportedException();

        public Task<string> AssembleContractAsync(
            Contract contract,
            List<(ContractClause ContractClause, Clause Clause)> clauses,
            Dictionary<string, string> fieldValues) =>
            throw new NotSupportedException();

        public Task<bool> TestConnectionAsync() => throw new NotSupportedException();

        public Task<OpenAiConnectionTestResult> TestConnectionDetailedAsync() => throw new NotSupportedException();
    }
}
