using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Regression tests for the contract owner.
/// <para>
/// <c>Contract.UserId</c> is a required foreign key to <c>Users.Id</c>, but the
/// application only ever sets the foreign key value when it creates a contract
/// (the wizard assigns <c>UserId = 1</c> and never attaches a <see cref="User"/>
/// instance). The owner row therefore has to exist before a contract can be
/// inserted, otherwise the insert fails with
/// "SQLite Error 19: 'FOREIGN KEY constraint failed'".
/// </para>
/// <para>
/// The rest of the suite never caught this because every test attaches the parent
/// entity (<c>User = new User { ... }</c>), which makes EF insert the owner row
/// as a side effect. These tests reproduce the production insert exactly: the
/// foreign key value alone, with no parent entity attached.
/// </para>
/// </summary>
public class ContractOwnerUserTests
{
    [Fact]
    public async Task MigratedDatabase_SeedsTheOwnerUser()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"draftengine_owner_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using (var context = new DraftEngineDbContext(options))
            {
                // Exactly what the application does at startup.
                await context.Database.MigrateAsync();

                var owner = await context.Users.SingleAsync();
                Assert.Equal(1, owner.Id);
                Assert.Equal("Local User", owner.DisplayName);
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task ContractWithUserIdOnly_Persists_WithoutAttachingTheOwner()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"draftengine_owner_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using (var context = new DraftEngineDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            using (var context = new DraftEngineDbContext(options))
            {
                // The wizard sets the foreign key and nothing else: no parent entity is
                // attached, so the insert only succeeds when the owner user already exists.
                context.Contracts.Add(new Contract
                {
                    UserId = 1,
                    Title = "Mutual NDA — Acme",
                    ContractType = "Mutual NDA",
                    ClientName = "Acme Inc",
                    ClientEmail = "legal@acme.example",
                    PartiesJson = "[]",
                    GoverningLaw = "Delaware",
                    TermMonths = 12,
                    Status = ContractStatus.Draft,
                });

                var affected = await context.SaveChangesAsync();

                Assert.Equal(1, affected);
                Assert.Equal(1, await context.Contracts.CountAsync());
                Assert.Equal(1, await context.Contracts.CountAsync(contract => contract.UserId == 1));
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task EnsureCreatedDatabase_SeedsTheOwnerUser()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new DraftEngineDbContext(options);
        await context.Database.EnsureCreatedAsync();

        Assert.Equal(1, await context.Users.CountAsync());
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var wal = path + "-wal";
            var shm = path + "-shm";
            if (File.Exists(wal)) File.Delete(wal);
            if (File.Exists(shm)) File.Delete(shm);
        }
        catch { /* best effort */ }
    }
}
