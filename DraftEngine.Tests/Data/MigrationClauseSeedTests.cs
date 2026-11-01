using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

public class MigrationClauseSeedTests
{
    [Fact]
    public async Task MigratedDatabase_HasAllClauses()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"draftengine_mig_{Guid.NewGuid():N}.db");
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
                var count = await context.Clauses.CountAsync();
                Assert.True(count >= 42);
                Assert.True(await context.Clauses.AnyAsync(c => c.Id == 13 && c.ContractType == "Non-Disclosure Agreement (One-Way)"));
                Assert.True(await context.Clauses.AnyAsync(c => c.Id == 23 && c.ContractType == "Independent Contractor Agreement"));
                Assert.True(await context.Clauses.AnyAsync(c => c.Id == 33 && c.ContractType == "Consulting Agreement"));
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
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
        catch { }
    }
}
