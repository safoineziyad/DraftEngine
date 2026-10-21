using System.Text.RegularExpressions;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Integrity checks over the seeded Mutual NDA clause library: every placeholder that appears
/// in a seeded <c>BodyTemplate</c> must be resolvable by <see cref="FieldService"/>, so a
/// freshly generated contract never ships a raw <c>{{Placeholder}}</c> to the user.
/// </summary>
public class SeededTemplateIntegrityTests : IDisposable
{
    private static readonly Regex PlaceholderPattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    private readonly SqliteConnection _connection;
    private readonly DraftEngineDbContext _dbContext;
    private readonly FieldService _fieldService = new();

    public SeededTemplateIntegrityTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(options);
        _dbContext.Database.EnsureCreated(); // schema + seeded Mutual NDA clause library
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AllSeededClauses_WithEmptyFieldValues_ContainNoRawPlaceholders()
    {
        var clauses = await _dbContext.Clauses.AsNoTracking()
            .Where(c => c.ContractType == "Mutual NDA")
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        Assert.Equal(12, clauses.Count); // the seeded library must not shrink silently

        foreach (var clause in clauses)
        {
            var filled = _fieldService.ReplacePlaceholders(
                clause.BodyTemplate, new Dictionary<string, string>());

            Assert.DoesNotContain("{{", filled, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AllSeededClauses_ExtractedPlaceholderKeys_AreKnownSupportedKeys()
    {
        // Whitelist mirrors FieldService.SupportedKeyDefaults: any seeded template that starts
        // using a key outside this set fails here, pointing straight at the offending clause.
        var supportedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "Party1", "Party2", "Party1Address", "Party2Address",
            "ClientName", "ClientEmail", "EffectiveDate", "GoverningLaw",
            "TermMonths", "Purpose", "ConfidentialInformation",
        };

        var clauses = await _dbContext.Clauses.AsNoTracking()
            .Where(c => c.ContractType == "Mutual NDA")
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        Assert.Equal(12, clauses.Count);

        foreach (var clause in clauses)
        {
            var keys = PlaceholderPattern.Matches(clause.BodyTemplate)
                .Select(match => match.Groups[1].Value);

            Assert.All(keys, key =>
                Assert.True(supportedKeys.Contains(key),
                    $"Clause '{clause.Title}' (id {clause.Id}) uses unsupported placeholder '{{{{{key}}}}}'."));
        }
    }
}
