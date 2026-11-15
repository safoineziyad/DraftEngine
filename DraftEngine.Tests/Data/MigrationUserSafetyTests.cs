using System.Text.RegularExpressions;
using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Fails if any migration's Up operations delete Users (or Contracts) rows or drop their
/// tables. Seed changes must update rows, never delete and re-insert them.
/// </summary>
public sealed class MigrationUserSafetyTests
{
    private static readonly string[] ProtectedTables = ["Users", "Contracts"];

    [Fact]
    public void NoMigration_DeletesOrDropsUserOrContractData()
    {
        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new DraftEngineDbContext(options);
        var assembly = context.GetService<IMigrationsAssembly>();

        Assert.NotEmpty(assembly.Migrations);

        var offenders = new List<string>();
        foreach (var (id, type) in assembly.Migrations)
        {
            var migration = (Migration)Activator.CreateInstance(type)!;
            foreach (var operation in migration.UpOperations)
            {
                switch (operation)
                {
                    case DeleteDataOperation delete when ProtectedTables.Contains(delete.Table):
                        offenders.Add($"{id}: DeleteData on {delete.Table}");
                        break;
                    case DropTableOperation drop when ProtectedTables.Contains(drop.Name):
                        offenders.Add($"{id}: DropTable {drop.Name}");
                        break;
                    case SqlOperation sql when DeletesOrDrops(sql.Sql):
                        offenders.Add($"{id}: SQL deletes {string.Join('/', ProtectedTables)}");
                        break;
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Destructive migration operations found: " + string.Join("; ", offenders));
    }

    private static bool DeletesOrDrops(string sql)
    {
        foreach (var table in ProtectedTables)
        {
            if (Regex.IsMatch(sql,
                    $@"\b(?:DELETE\s+FROM|DROP\s+TABLE|TRUNCATE\s+TABLE)\s+[\[\x22\x60]?{table}\b",
                    RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
