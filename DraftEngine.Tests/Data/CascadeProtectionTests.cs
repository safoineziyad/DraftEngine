using DraftEngine.Core.Models;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Regression for the cascade chain: every foreign key that points at <see cref="User"/>
/// must restrict deletion, so removing (or replacing) the local user can never erase the
/// contracts and their history.
/// </summary>
public sealed class CascadeProtectionTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "DraftEngineCascade", Guid.NewGuid().ToString("N") + ".db");

    public CascadeProtectionTests() => Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

    [Fact]
    public async Task EveryForeignKeyToUsers_RestrictsDelete()
    {
        await using var context = CreateContext();

        var userForeignKeys = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(key => key.PrincipalEntityType.ClrType == typeof(User))
            .ToArray();

        Assert.NotEmpty(userForeignKeys);
        Assert.All(userForeignKeys, key =>
            Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
    }

    [Fact]
    public async Task DeletingTheUser_IsBlockedAndContractsSurvive()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        await using (var connection = Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Contracts (
                    UserId, Title, ContractType, Status, ClientName, ClientEmail, PartiesJson,
                    EffectiveDate, GoverningLaw, TermMonths, CreatedAt, UpdatedAt,
                    LastGeneratedAt, FinalFilePath, AssembledText)
                VALUES (
                    1, 'Cascade Guard Contract', 'MutualNda', 1, 'Acme', '', '[]',
                    NULL, 'California', 12, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z',
                    NULL, '', '1. Confidentiality');
                """;
            command.ExecuteNonQuery();
        }

        await using (var context = CreateContext())
        {
            var user = await context.Users.SingleAsync(account => account.Id == 1);
            context.Users.Remove(user);

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.Contracts.CountAsync());
        Assert.Equal("Cascade Guard Contract", (await verify.Contracts.SingleAsync()).Title);
    }

    private DraftEngineDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = _path,
                Pooling = false,
                ForeignKeys = true
            }.ConnectionString)
            .Options);

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Pooling = false,
            ForeignKeys = true
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_path);
            File.Delete(_path + "-wal");
            File.Delete(_path + "-shm");
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
