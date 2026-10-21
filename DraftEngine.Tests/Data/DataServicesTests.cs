using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for the DbContext-backed data services. Each test runs against its own
/// in-memory SQLite database (schema + seeded clause library via EnsureCreated).
/// </summary>
public class DataServicesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DraftEngineDbContext _dbContext;
    private readonly TestDatabaseContextFactory _databaseFactory;

    public DataServicesTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(options);
        _dbContext.Database.EnsureCreated(); // schema + seeded Mutual NDA clause library

        // Same pattern as production: services open one short-lived context per operation.
        _databaseFactory = new TestDatabaseContextFactory(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------- ClauseService

    [Fact]
    public async Task ClauseService_ReturnsClausesForContractType_InSortOrder()
    {
        var service = new ClauseService(_databaseFactory);

        var clauses = await service.GetClausesByContractTypeAsync("Mutual NDA");

        Assert.Equal(12, clauses.Count); // seeded library
        Assert.Equal(clauses.OrderBy(c => c.SortOrder).Select(c => c.Id), clauses.Select(c => c.Id));
        Assert.All(clauses, c => Assert.Equal("Mutual NDA", c.ContractType));
    }

    [Fact]
    public async Task ClauseService_UnknownContractType_ReturnsEmptyList()
    {
        var service = new ClauseService(_databaseFactory);

        Assert.Empty(await service.GetClausesByContractTypeAsync("Lease Agreement"));
        Assert.Empty(await service.GetClausesByContractTypeAsync(""));
    }

    [Fact]
    public async Task ClauseService_DefaultSelection_FiltersOnIsDefaultSelected()
    {
        _dbContext.Clauses.Add(new Clause
        {
            ContractType = "Mutual NDA",
            Category = "Extras",
            Title = "Non-default clause",
            BodyTemplate = "Body",
            IsOptional = true,
            IsDefaultSelected = false,
            SortOrder = 99
        });
        await _dbContext.SaveChangesAsync();

        var service = new ClauseService(_databaseFactory);
        var defaults = await service.GetDefaultSelectedClausesAsync("Mutual NDA");

        Assert.Equal(12, defaults.Count);
        Assert.DoesNotContain(defaults, c => c.Title == "Non-default clause");
    }

    // ---------------------------------------------------------------- FieldService

    [Fact]
    public async Task FieldService_MapFields_PersistsAnswers_AndReturnsStoredRows()
    {
        var contract = await SeedContractAsync();
        var service = new FieldService(_databaseFactory);

        var values = await service.MapFieldsAsync(
            contract.Id,
            new Dictionary<string, string> { ["Party2"] = "Globex Corp" });

        Assert.Equal("Globex Corp", values["Party2"]);

        // The answer was persisted as a ContractField row.
        var stored = await _dbContext.ContractFields
            .Where(f => f.ContractId == contract.Id && f.Key == "Party2")
            .ToListAsync();
        Assert.Single(stored);
        Assert.Equal("Globex Corp", stored[0].Value);
    }

    [Fact]
    public async Task FieldService_MapFields_UpdatingSameKey_DoesNotDuplicateRows()
    {
        var contract = await SeedContractAsync();
        var service = new FieldService(_databaseFactory);

        await service.MapFieldsAsync(contract.Id, new Dictionary<string, string> { ["Party2"] = "First" });
        await service.MapFieldsAsync(contract.Id, new Dictionary<string, string> { ["Party2"] = "Second" });

        var rows = await _dbContext.ContractFields
            .Where(f => f.ContractId == contract.Id && f.Key == "Party2")
            .ToListAsync();
        Assert.Single(rows);
        Assert.Equal("Second", rows[0].Value);
    }

    [Fact]
    public async Task FieldService_MapFields_EmptyFormValues_ReturnsEmpty()
    {
        var contract = await SeedContractAsync();
        var service = new FieldService(_databaseFactory);

        var values = await service.MapFieldsAsync(contract.Id, new Dictionary<string, string>());

        Assert.Empty(values);
    }

    [Fact]
    public void FieldService_ReplacesPlaceholders_SubstitutesKnownKeys_AndDefaults()
    {
        var fieldService = new FieldService(_databaseFactory);

        var resolved = fieldService.ReplacePlaceholders(
            "This Agreement is between {{Party1}} and {{Party2}} for {{TermMonths}} months. {{Answered}}",
            new Dictionary<string, string> { ["Party2"] = "Globex Corp" });

        // Provided values win, known keys fall back to safe defaults, unknown keys stay untouched.
        Assert.Equal(
            "This Agreement is between the Disclosing Party and Globex Corp for 12 months. {{Answered}}",
            resolved);
    }

    [Fact]
    public void FieldService_ReplacesPlaceholders_FormatsEffectiveDateAsLongForm()
    {
        var fieldService = new FieldService(_databaseFactory);

        var resolved = fieldService.ReplacePlaceholders(
            "as of {{EffectiveDate}}",
            new Dictionary<string, string> { ["EffectiveDate"] = "2026-01-15" });

        Assert.Equal("as of January 15, 2026", resolved);
    }

    // ---------------------------------------------------------------- FieldService edge cases

    [Fact]
    public async Task FieldService_MapFields_UnknownKeys_ArePersistedAndReturned()
    {
        var contract = await SeedContractAsync();
        var service = new FieldService(_databaseFactory);

        var values = await service.MapFieldsAsync(
            contract.Id,
            new Dictionary<string, string> { ["FavoriteColor"] = "Blue" });

        // Free-form intake answers are stored verbatim, even when no placeholder uses them.
        Assert.Equal("Blue", values["FavoriteColor"]);
        var row = await _dbContext.ContractFields.AsNoTracking()
            .SingleAsync(f => f.ContractId == contract.Id && f.Key == "FavoriteColor");
        Assert.Equal("Blue", row.Value);
    }

    [Fact]
    public async Task FieldService_MapFields_NullValue_IsStoredAsEmptyString()
    {
        var contract = await SeedContractAsync();
        var service = new FieldService(_databaseFactory);

        var values = await service.MapFieldsAsync(
            contract.Id,
            new Dictionary<string, string> { ["Party2"] = null! });

        Assert.Equal(string.Empty, values["Party2"]);
        var row = await _dbContext.ContractFields.AsNoTracking()
            .SingleAsync(f => f.ContractId == contract.Id && f.Key == "Party2");
        Assert.Equal(string.Empty, row.Value);
    }

    [Fact]
    public async Task FieldService_MapFields_WhitespaceKey_IsSkipped()
    {
        var contract = await SeedContractAsync();
        var service = new FieldService(_databaseFactory);

        var values = await service.MapFieldsAsync(
            contract.Id,
            new Dictionary<string, string> { ["   "] = "junk", ["Party2"] = "Globex Corp" });

        Assert.False(values.ContainsKey("   "));
        Assert.Equal("Globex Corp", values["Party2"]);
        Assert.Equal(1, await _dbContext.ContractFields.AsNoTracking()
            .CountAsync(f => f.ContractId == contract.Id));
    }

    [Fact]
    public async Task FieldService_MapFields_KeyIsTrimmed_AndMatchedCaseInsensitively()
    {
        var contract = await SeedContractAsync();
        var service = new FieldService(_databaseFactory);

        await service.MapFieldsAsync(contract.Id, new Dictionary<string, string> { ["Party2"] = "First" });
        await service.MapFieldsAsync(contract.Id, new Dictionary<string, string> { [" party2 "] = "Second" });

        // The padded/lowercased key matches the stored row instead of creating a duplicate.
        var rows = await _dbContext.ContractFields.AsNoTracking()
            .Where(f => f.ContractId == contract.Id)
            .ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("Party2", row.Key);
        Assert.Equal("Second", row.Value);
    }

    [Fact]
    public async Task FieldService_MapFields_NullFormValues_ReturnsStoredRowsOnly()
    {
        var contract = await SeedContractAsync();
        _dbContext.ContractFields.Add(new ContractField { ContractId = contract.Id, Key = "Purpose", Value = "Evaluation" });
        await _dbContext.SaveChangesAsync();

        var service = new FieldService(_databaseFactory);
        var values = await service.MapFieldsAsync(contract.Id, null!);

        Assert.Equal("Evaluation", values["Purpose"]);
    }

    [Fact]
    public void FieldService_ReplacesPlaceholders_LookupIsCaseInsensitive()
    {
        var fieldService = new FieldService();

        var resolved = fieldService.ReplacePlaceholders(
            "between {{Party1}} and {{party2}}",
            new Dictionary<string, string> { ["party2"] = "Globex Corp" });

        Assert.Equal("between the Disclosing Party and Globex Corp", resolved);
    }

    [Fact]
    public void FieldService_ReplacesPlaceholders_WhitespaceValue_FallsBackToDefault()
    {
        var fieldService = new FieldService();

        var resolved = fieldService.ReplacePlaceholders(
            "{{Party2}} accepts delivery.",
            new Dictionary<string, string> { ["Party2"] = "   " });

        // A blank answer counts as missing, so the safe default is substituted instead.
        Assert.Equal("the Receiving Party accepts delivery.", resolved);
    }

    [Fact]
    public void FieldService_ReplacesPlaceholders_EffectiveDatePassThroughValues_AreStable()
    {
        var fieldService = new FieldService();

        var alreadyLongForm = fieldService.ReplacePlaceholders(
            "as of {{EffectiveDate}}",
            new Dictionary<string, string> { ["EffectiveDate"] = "January 15, 2026" });
        var unparseable = fieldService.ReplacePlaceholders(
            "as of {{EffectiveDate}}",
            new Dictionary<string, string> { ["EffectiveDate"] = "TBD" });

        // Values that are already long-form (or are not dates at all) pass through unchanged.
        Assert.Equal("as of January 15, 2026", alreadyLongForm);
        Assert.Equal("as of TBD", unparseable);
    }

    [Fact]
    public void FieldService_ReplacesPlaceholders_LowercaseEffectiveDateKey_SkipsLongFormFormatting()
    {
        var fieldService = new FieldService();

        var resolved = fieldService.ReplacePlaceholders(
            "as of {{effectivedate}}",
            new Dictionary<string, string> { ["EffectiveDate"] = "2026-01-15" });

        // Actual contract: the lookup is case-insensitive, but the long-form date formatting
        // only triggers for the canonical "EffectiveDate" spelling, so the ISO value passes
        // through raw. Seeded templates always use the canonical casing (guarded by
        // SeededTemplateIntegrityTests); see AGENT-A-REPORT.md for the inconsistency note.
        Assert.Equal("as of 2026-01-15", resolved);
    }

    [Fact]
    public void FieldService_ReplacesPlaceholders_NullOrEmptyTemplate_ReturnsEmpty()
    {
        var fieldService = new FieldService();

        Assert.Equal(string.Empty, fieldService.ReplacePlaceholders(null!, new Dictionary<string, string>()));
        Assert.Equal(string.Empty, fieldService.ReplacePlaceholders(string.Empty, new Dictionary<string, string>()));
    }

    // ---------------------------------------------------------------- ChatService

    [Fact]
    public async Task ChatService_AddAndGetMessages_RoundTripsInChronologicalOrder()
    {
        var contract = await SeedContractAsync();
        var service = new ChatService(_databaseFactory);

        await service.AddMessageAsync(contract.Id, MessageRole.User, "First");
        await service.AddMessageAsync(contract.Id, MessageRole.Assistant, "Second");

        var messages = await service.GetMessagesAsync(contract.Id);

        Assert.Equal(2, messages.Count);
        Assert.Equal(new[] { "First", "Second" }, messages.Select(m => m.Content));
        Assert.Equal(new[] { MessageRole.User, MessageRole.Assistant }, messages.Select(m => m.Role));
    }

    [Fact]
    public async Task ChatService_AddMessage_UnknownContract_Throws()
    {
        var service = new ChatService(_databaseFactory);

        // The FK constraint on ChatMessages.ContractId rejects the orphaned message.
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.AddMessageAsync(999, MessageRole.User, "hello"));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Seeds a user + Mutual NDA contract with deterministic values and returns the contract.</summary>
    private async Task<Contract> SeedContractAsync()
    {
        var user = new User
        {
            Email = "user@example.com",
            DisplayName = "Test User",
            OpenAiApiKeyEncrypted = string.Empty
        };

        var contract = new Contract
        {
            User = user,
            Title = "Test NDA",
            ContractType = "Mutual NDA",
            ClientName = "Acme Inc",
            ClientEmail = "legal@acme.example",
            EffectiveDate = new DateTime(2026, 1, 15),
            GoverningLaw = "Delaware",
            TermMonths = 12
        };

        _dbContext.Contracts.Add(contract);
        await _dbContext.SaveChangesAsync();
        return contract;
    }
}
