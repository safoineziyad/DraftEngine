using DraftEngine.App.ViewModels;
using DraftEngine.Core.Models;
using DraftEngine.Core.Models.ContractTypes;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

public sealed class ClauseManagementServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<DraftEngineDbContext> _options;
    private readonly TestDatabaseContextFactory _factory;
    private readonly ClauseManagementService _service;

    public ClauseManagementServiceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var database = new DraftEngineDbContext(_options))
            database.Database.EnsureCreated();

        _factory = new TestDatabaseContextFactory(_options);
        _service = new ClauseManagementService(_factory);
    }

    [Fact]
    public async Task GetClausesAsync_FiltersByTypeCategoryAndSource()
    {
        await _service.CreateUserClauseAsync(ContractTypeRegistry.MutualNdaId, NewClause("Custom", "User clause"));

        var users = await _service.GetClausesAsync("Mutual NDA", "Custom", "User");
        var system = await _service.GetClausesAsync("Mutual NDA", source: "System");

        Assert.Single(users);
        Assert.Equal("User clause", users[0].Title);
        Assert.NotEmpty(system);
        Assert.All(system, clause => Assert.Equal("System", clause.Source));
    }

    [Fact]
    public async Task ClauseManagerViewModel_FiltersAndKeepsSystemClausesReadOnly()
    {
        await _service.CreateUserClauseAsync(
            ContractTypeRegistry.MutualNdaId, NewClause("Manager test", "Manager user clause"));
        var viewModel = new ClauseManagerViewModel(_service);
        viewModel.OnNavigatedTo(null);
        await WaitForLoadAsync(viewModel);

        viewModel.SelectedSourceFilter = "User";
        viewModel.SelectedCategoryFilter = "Manager test";
        Assert.Single(viewModel.Clauses);
        Assert.Equal("Manager user clause", viewModel.Clauses[0].Title);

        viewModel.SelectedCategoryFilter = "All";
        viewModel.SelectedSourceFilter = "System";
        Assert.NotEmpty(viewModel.Clauses);
        viewModel.SelectedClause = viewModel.Clauses[0];
        Assert.False(viewModel.CanEditSelected);
        Assert.True(viewModel.CanDuplicateSelected);
        Assert.False(viewModel.CanArchiveSelected);
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public async Task CreateAndUpdateUserClause_PersistUserOwnedFieldsOnly()
    {
        var input = NewClause("Custom", "Original");
        input.Source = "System";
        input.OwnerUserId = null;
        input.IsSystem = true;
        input.IsUserDefined = false;
        var created = await _service.CreateUserClauseAsync("Mutual NDA", input);

        Assert.Equal(ContractTypeRegistry.MutualNdaId, created.ContractType);
        Assert.Equal("User", created.Source);
        Assert.Equal(1, created.OwnerUserId);
        Assert.False(created.IsSystem);
        Assert.True(created.IsUserDefined);

        var updated = await _service.UpdateUserClauseAsync(created.Id, NewClause("Updated", "Changed"));

        Assert.NotNull(updated);
        Assert.Equal("Updated", updated.Category);
        Assert.Equal("Changed", updated.Title);
        Assert.Equal("User", updated.Source);
        Assert.Equal(1, updated.OwnerUserId);
    }

    [Fact]
    public async Task SeededClause_IsReadOnlyButCanBeDuplicated()
    {
        var seeded = (await _service.GetClausesAsync(ContractTypeRegistry.MutualNdaId, source: "System"))
            .First();
        var originalTitle = seeded.Title;

        var update = await _service.UpdateUserClauseAsync(seeded.Id, NewClause("Changed", "Modified seed"));
        await _service.ArchiveUserClauseAsync(seeded.Id);
        await _service.DuplicateClauseAsync(seeded.Id, ContractTypeRegistry.MutualNdaId);

        using var database = new DraftEngineDbContext(_options);
        var unchangedSeed = await database.Clauses.SingleAsync(clause => clause.Id == seeded.Id);
        var duplicate = await database.Clauses
            .SingleAsync(clause => clause.Source == "User" && clause.Title == $"Copy of {originalTitle}");
        Assert.Null(update);
        Assert.Equal(originalTitle, unchangedSeed.Title);
        Assert.False(unchangedSeed.IsArchived);
        Assert.Equal("User", duplicate.Source);
        Assert.False(duplicate.IsSystem);
        Assert.True(duplicate.IsUserDefined);
        Assert.Equal(seeded.BodyTemplate, duplicate.BodyTemplate);
    }

    [Fact]
    public async Task ArchiveUserClause_HidesItFromWizardAndPreservesInUseClause()
    {
        var clause = await _service.CreateUserClauseAsync(
            ContractTypeRegistry.MutualNdaId, NewClause("Custom", "In-use clause"));
        await AddContractReferenceAsync(clause.Id);

        await _service.ArchiveUserClauseAsync(clause.Id);

        Assert.DoesNotContain(
            await new ClauseService(_factory).GetClausesByContractTypeAsync(ContractTypeRegistry.MutualNdaId),
            item => item.Id == clause.Id);
        using (var database = new DraftEngineDbContext(_options))
        {
            Assert.True((await database.Clauses.SingleAsync(item => item.Id == clause.Id)).IsArchived);
            Assert.Equal(1, await database.ContractClauses.CountAsync(item => item.ClauseId == clause.Id));
        }

        using var deleteContext = new DraftEngineDbContext(_options);
        var inUseClause = await deleteContext.Clauses.SingleAsync(item => item.Id == clause.Id);
        deleteContext.Clauses.Remove(inUseClause);
        await Assert.ThrowsAsync<DbUpdateException>(() => deleteContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ValidatePlaceholdersAsync_UnknownPlaceholder_IsDetected()
    {
        var (isValid, unknown) = await _service.ValidatePlaceholdersAsync("Hello {{Party1}}, meet {{MysteryGuest}}.");

        Assert.False(isValid);
        Assert.Contains("MysteryGuest", unknown);
    }

    [Fact]
    public async Task ValidatePlaceholdersAsync_AllKnown_IsValid()
    {
        var (isValid, unknown) = await _service.ValidatePlaceholdersAsync("{{Party1}} and {{Party2}}.");

        Assert.True(isValid);
        Assert.Empty(unknown);
    }

    [Fact]
    public async Task ValidatePlaceholdersAsync_Empty_IsValid()
    {
        var (isValid, unknown) = await _service.ValidatePlaceholdersAsync(string.Empty);

        Assert.True(isValid);
        Assert.Empty(unknown);
    }

    public void Dispose() => _connection.Dispose();

    private static async Task WaitForLoadAsync(ClauseManagerViewModel viewModel)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (viewModel.IsLoading && DateTime.UtcNow < timeout)
            await Task.Delay(1);
        Assert.False(viewModel.IsLoading);
    }

    private static Clause NewClause(string category, string title) => new()
    {
        Category = category,
        Title = title,
        BodyTemplate = "Clause text for {{Party1}}.",
        SortOrder = 10000
    };

    private async Task AddContractReferenceAsync(int clauseId)
    {
        using var database = new DraftEngineDbContext(_options);
        var contract = new Contract
        {
            UserId = 1,
            Title = "Clause reference test",
            ContractType = ContractTypeRegistry.MutualNdaId
        };
        database.Contracts.Add(contract);
        await database.SaveChangesAsync();
        database.ContractClauses.Add(new ContractClause
        {
            ContractId = contract.Id,
            ClauseId = clauseId,
            CustomBodyOverride = string.Empty
        });
        await database.SaveChangesAsync();
    }
}
