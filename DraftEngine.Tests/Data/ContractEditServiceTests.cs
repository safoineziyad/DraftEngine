using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

public sealed class ContractEditServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DraftEngineDbContext _dbContext;
    private readonly TestDatabaseContextFactory _databaseFactory;
    private readonly IContractEditService _editService;
    private readonly ContractService _contractService;

    public ContractEditServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(options);
        _dbContext.Database.EnsureCreated();

        _databaseFactory = new TestDatabaseContextFactory(options);
        _editService = new ContractEditService(_databaseFactory);
        _contractService = new ContractService(_databaseFactory, new FieldService(_databaseFactory));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetArchivedContractsAsync_FiltersByUserAndArchiveStatus()
    {
        var active = await CreateContractAsync("owner@example.com", "active");
        var archivedOlder = await CreateContractAsync("owner@example.com", "archived older");
        var archivedNewer = await CreateContractAsync("owner@example.com", "archived newer");
        await CreateContractAsync("other@example.com", "other user's archive", archived: true);

        archivedOlder.IsArchived = true;
        archivedOlder.ArchivedAt = DateTime.UtcNow.AddMinutes(-2);
        archivedOlder.CreatedAt = DateTime.UtcNow.AddDays(-1);
        archivedNewer.IsArchived = true;
        archivedNewer.ArchivedAt = DateTime.UtcNow.AddMinutes(-1);
        archivedNewer.CreatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var archived = await _editService.GetArchivedContractsAsync(active.UserId);

        Assert.Equal(new[] { archivedNewer.Id, archivedOlder.Id }, archived.Select(contract => contract.Id));
    }

    [Fact]
    public async Task ArchiveAndRestoreContractAsync_SetsAndClearsArchiveStateIdempotently()
    {
        var contract = await CreateContractAsync("owner@example.com", "archive/restore");

        await _editService.ArchiveContractAsync(contract.Id);
        var archivedAt = await _dbContext.Contracts.AsNoTracking()
            .Where(item => item.Id == contract.Id)
            .Select(item => item.ArchivedAt)
            .SingleAsync();

        Assert.NotNull(archivedAt);
        Assert.True(await _dbContext.Contracts.AsNoTracking()
            .Where(item => item.Id == contract.Id)
            .Select(item => item.IsArchived)
            .SingleAsync());

        await _editService.ArchiveContractAsync(contract.Id);
        Assert.Equal(archivedAt, await _dbContext.Contracts.AsNoTracking()
            .Where(item => item.Id == contract.Id)
            .Select(item => item.ArchivedAt)
            .SingleAsync());

        Assert.True(await _editService.RestoreContractAsync(contract.Id));
        Assert.False(await _editService.RestoreContractAsync(contract.Id));
        var restored = await _dbContext.Contracts.AsNoTracking().SingleAsync(item => item.Id == contract.Id);
        Assert.False(restored.IsArchived);
        Assert.Null(restored.ArchivedAt);
    }

    [Fact]
    public async Task GetAllAsync_ExcludesArchivedContracts()
    {
        var active = await CreateContractAsync("owner@example.com", "active");
        var archived = await CreateContractAsync("owner@example.com", "archived", archived: true);

        var contracts = await _contractService.GetAllAsync(active.UserId);

        Assert.Contains(contracts, item => item.Id == active.Id);
        Assert.DoesNotContain(contracts, item => item.Id == archived.Id);
    }

    [Fact]
    public async Task DuplicateContractAsync_ReturnsNullWhenSourceContractIsMissing()
    {
        var duplicate = await _editService.DuplicateContractAsync(int.MaxValue, "Missing source copy");

        Assert.Null(duplicate);
        Assert.False(await _dbContext.Contracts.AnyAsync());
    }

    [Fact]
    public async Task UpdateContractAsync_ReturnsNullWhenContractIsMissing()
    {
        var updated = await _editService.UpdateContractAsync(int.MaxValue, contract =>
            contract.Title = "Should not be saved");

        Assert.Null(updated);
        Assert.False(await _dbContext.Contracts.AnyAsync());
    }

    [Fact]
    public async Task PermanentDeleteContractAsync_RemovesContractAndAllOwnedData_AndIsIdempotent()
    {
        var contract = await CreateContractAsync("owner@example.com", "permanent delete", archived: true);
        var clause = new Clause
        {
            ContractType = "MutualNda",
            Category = "Test",
            Title = "Test clause",
            BodyTemplate = "Test body",
            Source = "System"
        };
        _dbContext.Clauses.Add(clause);
        await _dbContext.SaveChangesAsync();

        _dbContext.ContractClauses.Add(new ContractClause
        {
            ContractId = contract.Id,
            ClauseId = clause.Id,
            Selected = true
        });
        _dbContext.ContractFields.Add(new ContractField { ContractId = contract.Id, Key = "Party", Value = "Acme" });
        _dbContext.ChatMessages.Add(new ChatMessage
        {
            ContractId = contract.Id,
            Role = MessageRole.User,
            Content = "Please update the term"
        });
        _dbContext.GeneratedDocuments.Add(new GeneratedDocument
        {
            ContractId = contract.Id,
            FilePath = "generated/contract.docx",
            FileType = FileType.Docx
        });
        _dbContext.ContractVersions.Add(new ContractVersion
        {
            ContractId = contract.Id,
            VersionNumber = 1,
            Text = "Previous text",
            Reason = "Test"
        });
        await _dbContext.SaveChangesAsync();

        await _editService.PermanentDeleteContractAsync(contract.Id);
        await _editService.PermanentDeleteContractAsync(contract.Id);

        Assert.False(await _dbContext.Contracts.AnyAsync(item => item.Id == contract.Id));
        Assert.False(await _dbContext.ContractClauses.AnyAsync(item => item.ContractId == contract.Id));
        Assert.False(await _dbContext.ContractFields.AnyAsync(item => item.ContractId == contract.Id));
        Assert.False(await _dbContext.ChatMessages.AnyAsync(item => item.ContractId == contract.Id));
        Assert.False(await _dbContext.GeneratedDocuments.AnyAsync(item => item.ContractId == contract.Id));
        Assert.False(await _dbContext.ContractVersions.AnyAsync(item => item.ContractId == contract.Id));
        Assert.True(await _dbContext.Clauses.AnyAsync(item => item.Id == clause.Id));
    }

    private async Task<Contract> CreateContractAsync(string email, string title, bool archived = false)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.Email == email);
        if (user is null)
        {
            user = new User
            {
                Email = email,
                DisplayName = "Test User",
                OpenAiApiKeyEncrypted = string.Empty
            };
            _dbContext.Users.Add(user);
        }

        var contract = new Contract
        {
            User = user,
            Title = title,
            ContractType = "MutualNda",
            ClientName = "Acme",
            ClientEmail = "legal@acme.example",
            IsArchived = archived,
            ArchivedAt = archived ? DateTime.UtcNow : null
        };
        _dbContext.Contracts.Add(contract);
        await _dbContext.SaveChangesAsync();
        return contract;
    }
}
