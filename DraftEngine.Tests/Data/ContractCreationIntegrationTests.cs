using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// End-to-end regression for the production "create a contract" path (A1).
/// <para>
/// The shipped application could not create a contract at all: <c>Contract.UserId</c>
/// is a required foreign key to <c>Users.Id</c>, the wizard writes only the foreign key
/// value, and nothing ever inserted the owner row, so every save failed with
/// "SQLite Error 19: 'FOREIGN KEY constraint failed'".
/// </para>
/// <para>
/// The rest of the suite missed it because it attaches the parent entity
/// (<c>User = new User { ... }</c>), which makes EF insert the owner as a side effect.
/// This test deliberately runs against a <b>migrated</b> file-backed database - the same
/// startup path the application uses - and never attaches a <see cref="User"/>.
/// </para>
/// </summary>
public class ContractCreationIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<DraftEngineDbContext> _options;

    public ContractCreationIntegrationTests()
    {
        // A real file-backed database (not :memory:) so migrations behave exactly as they
        // do in the application.
        _dbPath = Path.Combine(Path.GetTempPath(), $"draftengine_create_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
    }

    public void Dispose() => TryDelete(_dbPath);

    [Fact]
    public async Task CreateContract_FullFlow_Succeeds()
    {
        // ---- Startup: migrate, exactly like DatabaseService.EnsureDatabaseCreated. ----
        using (var context = new DraftEngineDbContext(_options))
        {
            await context.Database.MigrateAsync();
        }

        var factory = new TestDatabaseContextFactory(_options);

        // ---- 1. The owner row and the clause library are present. ----
        int ownerId;
        List<int> clauseIds;
        using (var context = new DraftEngineDbContext(_options))
        {
            Assert.Equal(1, await context.Users.CountAsync());

            var owner = await context.Users.SingleAsync();
            ownerId = owner.Id;
            Assert.Equal(1, ownerId);

            clauseIds = await context.Clauses
                .Where(clause => clause.ContractType == "Mutual NDA")
                .OrderBy(clause => clause.SortOrder)
                .Select(clause => clause.Id)
                .ToListAsync();

            Assert.Equal(12, clauseIds.Count);
        }

        // ---- 2. Create the contract the way the wizard does: foreign key only. ----
        int contractId;
        using (var context = new DraftEngineDbContext(_options))
        {
            var contract = new Contract
            {
                UserId = ownerId, // no User instance attached on purpose
                Title = "Mutual NDA - Acme Corp & Globex LLC",
                ContractType = "Mutual NDA",
                ClientName = "Jane Doe",
                ClientEmail = "jane@example.com",
                PartiesJson = """
                    [{"name":"Acme Corp","address":"1 Market Street"},{"name":"Globex LLC","address":"2 Market Street"}]
                    """,
                EffectiveDate = new DateTime(2026, 1, 15),
                GoverningLaw = "Delaware",
                TermMonths = 24,
                Status = ContractStatus.Draft
            };

            context.Contracts.Add(contract);
            await context.SaveChangesAsync();

            contractId = contract.Id;
            Assert.True(contractId > 0);

            // ---- 3. Attach the clauses the user selected. ----
            foreach (var clauseId in clauseIds)
            {
                context.ContractClauses.Add(new ContractClause
                {
                    ContractId = contractId,
                    ClauseId = clauseId,
                    Selected = true
                });
            }

            await context.SaveChangesAsync();
        }

        // ---- 4/5. Generate: must not throw. ----
        var service = new ContractService(factory, new FieldService(factory));
        var generated = await service.GenerateContractAsync(contractId, clauseIds);

        Assert.False(string.IsNullOrWhiteSpace(generated));

        // ---- 6. Reload from the database: the assembled text was persisted. ----
        string assembledText;
        using (var context = new DraftEngineDbContext(_options))
        {
            var reloaded = await context.Contracts.SingleAsync(contract => contract.Id == contractId);
            assembledText = reloaded.AssembledText;

            Assert.False(string.IsNullOrWhiteSpace(assembledText));
            Assert.NotNull(reloaded.LastGeneratedAt);
        }

        // ---- 7. No unresolved {{...}} placeholder survived into the document. ----
        Assert.DoesNotContain("{{", assembledText);
        Assert.Contains("Acme Corp", assembledText);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
        catch
        {
            // Best effort: a leaked temp file must not fail the build.
        }
    }
}
