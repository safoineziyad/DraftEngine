using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DraftEngine.Tests.Data;

public class MigrationClauseSeedTests
{
    [Fact]
    public async Task UpgradeFromContractVersionHistory_PreservesContractAndAppliesRemainingMigrations()
    {
        var dbPath = Path.Combine(AppContext.BaseDirectory, $"draftengine_mig_{Guid.NewGuid():N}.db");
        const string title = "Migration preservation sample";
        const string contractText = "Existing mutual NDA text must remain unchanged.";
        var contractId = 0;

        try
        {
            var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
                .UseSqlite($"Data Source={dbPath};Pooling=False")
                .Options;

            await using (var context = new DraftEngineDbContext(options))
            {
                await context.GetService<IMigrator>()
                    .MigrateAsync("20261021133317_AddContractVersionHistory");
                contractId = await InsertBaselineContractAsync(context, title, contractText);
            }

            await using (var context = new DraftEngineDbContext(options))
            {
                await context.Database.MigrateAsync();

                var clauseIds = await context.Clauses
                    .AsNoTracking()
                    .Select(clause => clause.Id)
                    .ToListAsync();
                Assert.All(Enumerable.Range(1, 42), id => Assert.Contains(id, clauseIds));
                Assert.True(await context.Clauses.AnyAsync(
                    clause => clause.Id == 13 && clause.ContractType == "OneWayNda"));
                Assert.True(await context.Clauses.AnyAsync(
                    clause => clause.Id == 23 && clause.ContractType == "IndependentContractorAgreement"));
                Assert.True(await context.Clauses.AnyAsync(
                    clause => clause.Id == 33 && clause.ContractType == "ConsultingAgreement"));

                var clauseColumns = await GetColumnNamesAsync(context, "Clauses");
                Assert.Contains("IsUserDefined", clauseColumns);
                Assert.Contains("IsArchived", clauseColumns);

                var contractColumns = await GetColumnNamesAsync(context, "Contracts");
                Assert.Contains("IsArchived", contractColumns);
                Assert.Contains("ArchivedAt", contractColumns);

                Assert.True(await TableExistsAsync(context, "WizardDrafts"));
                var wizardDraftColumns = await GetColumnNamesAsync(context, "WizardDrafts");
                Assert.Contains("EncryptedPayload", wizardDraftColumns);

                var savedContract = await context.Contracts
                    .AsNoTracking()
                    .SingleAsync(contract => contract.Id == contractId);
                Assert.Equal(title, savedContract.Title);
                Assert.Equal(contractText, savedContract.AssembledText);
                Assert.Equal("MutualNda", savedContract.ContractType);
            }
        }
        finally
        {
            TryDelete(dbPath);
            TryDelete(dbPath + "-wal");
            TryDelete(dbPath + "-shm");
        }
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(
        DraftEngineDbContext context,
        string tableName)
    {
        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State == System.Data.ConnectionState.Closed;
        if (wasClosed)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{tableName}');";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }

            return columns;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<int> InsertBaselineContractAsync(
        DraftEngineDbContext context,
        string title,
        string contractText)
    {
        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State == System.Data.ConnectionState.Closed;
        if (wasClosed)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO Contracts (
                        UserId, Title, ContractType, Status, ClientName, ClientEmail, PartiesJson,
                        EffectiveDate, GoverningLaw, TermMonths, CreatedAt, UpdatedAt,
                        LastGeneratedAt, FinalFilePath, AssembledText
                    ) VALUES (
                        @userId, @title, 'Mutual NDA', 0, 'Migration Test Client',
                        'migration@example.test', '{}', NULL, 'California', 0,
                        @createdAt, @updatedAt, NULL, '', @assembledText
                    );
                    """;
                AddParameter(command, "@userId", 1);
                AddParameter(command, "@title", title);
                AddParameter(command, "@createdAt", DateTime.UtcNow);
                AddParameter(command, "@updatedAt", DateTime.UtcNow);
                AddParameter(command, "@assembledText", contractText);
                await command.ExecuteNonQueryAsync();
            }

            await using var idCommand = connection.CreateCommand();
            idCommand.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(await idCommand.ExecuteScalarAsync());
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> TableExistsAsync(
        DraftEngineDbContext context,
        string tableName)
    {
        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State == System.Data.ConnectionState.Closed;
        if (wasClosed)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @tableName;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }
}
