using System.Text.RegularExpressions;
using DraftEngine.Core.Models.ContractTypes;
using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DraftEngine.Tests.Data;

public class ContractTypeRegistryTests
{
    private static readonly Regex PlaceholderPattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);
    private readonly ITestOutputHelper _output;

    public ContractTypeRegistryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Registry_HasFourStableDefinitionsWithRequiredIntakeAndSeededDefaultClauses()
    {
        var definitions = ContractTypeRegistry.All;
        var expectedIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "MutualNda",
            "OneWayNda",
            "IndependentContractorAgreement",
            "ConsultingAgreement"
        };

        Assert.Equal(4, definitions.Count);
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.Id).Distinct().Count());
        Assert.Equal(
            expectedIds.OrderBy(id => id, StringComparer.Ordinal),
            definitions.Select(definition => definition.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(definitions, definition =>
            Assert.Contains(definition.IntakeFields, field => field.Required));

        var dbPath = GetDatabasePath();
        try
        {
            var options = CreateOptions(dbPath);
            HashSet<int> seededClauseIds;
            await using (var context = new DraftEngineDbContext(options))
            {
                await context.Database.MigrateAsync();
                seededClauseIds = (await context.Clauses
                    .AsNoTracking()
                    .Select(clause => clause.Id)
                    .ToListAsync()).ToHashSet();
            }

            foreach (var definition in definitions)
            {
                Assert.NotEmpty(definition.DefaultClauseIds);
                foreach (var clauseId in definition.DefaultClauseIds)
                {
                    Assert.Contains(clauseId, seededClauseIds);
                }
            }
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    [Fact]
    public void LegacyOneWayName_ResolvesToCanonicalOneWayDefinition()
    {
        var canonical = ContractTypeRegistry.GetById("OneWayNda");
        var legacy = ContractTypeRegistry.TryGetByDisplayName("Non-Disclosure Agreement (One-Way)");

        Assert.Same(canonical, legacy);
    }

    [Fact]
    public async Task SeededClausePlaceholderAudit_ReportsUnknownKeysWithoutFailingForMismatches()
    {
        var dbPath = GetDatabasePath();
        try
        {
            var options = CreateOptions(dbPath);
            List<DraftEngine.Core.Models.Clause> clauses;
            await using (var context = new DraftEngineDbContext(options))
            {
                await context.Database.MigrateAsync();
                clauses = await context.Clauses.AsNoTracking().ToListAsync();
            }

            var mismatchCount = 0;
            foreach (var definition in ContractTypeRegistry.All)
            {
                var typeClauses = clauses
                    .Where(clause => clause.ContractType == definition.Id)
                    .ToList();
                Assert.NotEmpty(typeClauses);

                foreach (var clause in typeClauses)
                {
                    var unknownKeys = PlaceholderPattern.Matches(clause.BodyTemplate)
                        .Select(match => match.Groups[1].Value)
                        .Distinct(StringComparer.Ordinal)
                        .Where(key => !definition.SupportedPlaceholderKeys.Contains(key))
                        .OrderBy(key => key, StringComparer.Ordinal)
                        .ToArray();

                    if (unknownKeys.Length > 0)
                    {
                        mismatchCount++;
                        _output.WriteLine(
                            $"Type={definition.Id}; ClauseId={clause.Id}; UnknownKeys={string.Join(", ", unknownKeys)}");
                    }
                }
            }

            if (mismatchCount == 0)
            {
                _output.WriteLine("Placeholder audit: no unknown keys found.");
            }
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    private static string GetDatabasePath() =>
        Path.Combine(AppContext.BaseDirectory, $"draftengine_contract_types_{Guid.NewGuid():N}.db");

    private static DbContextOptions<DraftEngineDbContext> CreateOptions(string dbPath) =>
        new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=False")
            .Options;

    private static void DeleteDatabaseFiles(string dbPath)
    {
        TryDelete(dbPath);
        TryDelete(dbPath + "-wal");
        TryDelete(dbPath + "-shm");
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
        catch
        {
        }
    }
}
