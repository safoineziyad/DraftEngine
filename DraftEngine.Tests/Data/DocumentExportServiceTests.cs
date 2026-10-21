using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for <see cref="DocumentExportService"/> — the export bookkeeping used by the
/// Contract Detail page: versioned <see cref="GeneratedDocument"/> records, the contract's
/// FinalFilePath, and the Exported status shown in the contracts list.
/// </summary>
public class DocumentExportServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DraftEngineDbContext _dbContext;
    private readonly TestDatabaseContextFactory _databaseFactory;
    private readonly DocumentExportService _service;

    public DocumentExportServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(options);
        _dbContext.Database.EnsureCreated();

        // Same pattern as production: services open one short-lived context per operation.
        _databaseFactory = new TestDatabaseContextFactory(options);
        _service = new DocumentExportService(_databaseFactory);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------- export bookkeeping

    [Fact]
    public async Task RecordExportAsync_CreatesRecord_SetsFinalFilePath_AndMarksExported()
    {
        var contract = await CreateContractAsync();
        Assert.Equal(ContractStatus.Generated, contract.Status);

        var document = await _service.RecordExportAsync(contract.Id, @"C:\exports\nda.docx", FileType.Docx);

        Assert.True(document.Id > 0);
        Assert.Equal(contract.Id, document.ContractId);
        Assert.Equal(@"C:\exports\nda.docx", document.FilePath);
        Assert.Equal(FileType.Docx, document.FileType);
        Assert.Equal(1, document.Version);
        Assert.True(document.GeneratedAt <= DateTime.UtcNow.AddMinutes(1));

        var persistedContract = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(@"C:\exports\nda.docx", persistedContract.FinalFilePath);
        Assert.Equal(ContractStatus.Exported, persistedContract.Status);

        var persistedDocument = await _dbContext.GeneratedDocuments.AsNoTracking().SingleAsync();
        Assert.Equal(FileType.Docx, persistedDocument.FileType);
        Assert.Equal(1, persistedDocument.Version);
    }

    [Fact]
    public async Task RecordExportAsync_SecondExport_IncrementsVersion_AndRepointsContract()
    {
        var contract = await CreateContractAsync();

        await _service.RecordExportAsync(contract.Id, @"C:\exports\nda-v1.docx", FileType.Docx);
        var second = await _service.RecordExportAsync(contract.Id, @"C:\exports\nda-v2.pdf", FileType.Pdf);

        Assert.Equal(2, second.Version);
        Assert.Equal(FileType.Pdf, second.FileType);

        var persistedContract = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(@"C:\exports\nda-v2.pdf", persistedContract.FinalFilePath);
        Assert.Equal(2, await _dbContext.GeneratedDocuments.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task RecordExportAsync_EmptyPath_ThrowsFriendlyMessage_AndStoresNothing()
    {
        var contract = await CreateContractAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RecordExportAsync(contract.Id, "  ", FileType.Pdf));

        Assert.Contains("did not produce a file path", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await _dbContext.GeneratedDocuments.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task RecordExportAsync_UnknownContract_ThrowsFriendlyMessage()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RecordExportAsync(9999, @"C:\exports\missing.docx", FileType.Docx));

        Assert.Contains("could not be found", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await _dbContext.GeneratedDocuments.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task RecordExportAsync_MultipleExports_AllRecordsAccumulate()
    {
        var contract = await CreateContractAsync();

        await _service.RecordExportAsync(contract.Id, @"C:\exports\v1.docx", FileType.Docx);
        await _service.RecordExportAsync(contract.Id, @"C:\exports\v2.docx", FileType.Docx);
        await _service.RecordExportAsync(contract.Id, @"C:\exports\v3.pdf", FileType.Pdf);

        var documents = await _dbContext.GeneratedDocuments
            .AsNoTracking()
            .Where(d => d.ContractId == contract.Id)
            .OrderBy(d => d.Version)
            .ToListAsync();

        Assert.Equal(3, documents.Count);
        Assert.Equal(new[] { 1, 2, 3 }, documents.Select(d => d.Version));
        Assert.Equal(new[] { FileType.Docx, FileType.Docx, FileType.Pdf }, documents.Select(d => d.FileType));

        // The contract always points at the newest export.
        var persistedContract = await _dbContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(@"C:\exports\v3.pdf", persistedContract.FinalFilePath);
        Assert.Equal(ContractStatus.Exported, persistedContract.Status);
    }

    [Fact]
    public async Task RecordExportAsync_VersionsAreIndependent_PerContract()
    {
        var contractA = await CreateContractAsync(email: "a@example.com");
        var contractB = await CreateContractAsync(email: "b@example.com");

        await _service.RecordExportAsync(contractA.Id, @"C:\exports\a-v1.docx", FileType.Docx);
        await _service.RecordExportAsync(contractA.Id, @"C:\exports\a-v2.pdf", FileType.Pdf);
        var firstForB = await _service.RecordExportAsync(contractB.Id, @"C:\exports\b-v1.docx", FileType.Docx);

        // Contract A having two exports must not inflate contract B's version counter.
        Assert.Equal(1, firstForB.Version);
        Assert.Equal(2, await _dbContext.GeneratedDocuments.AsNoTracking().CountAsync(d => d.ContractId == contractB.Id));
    }

    [Fact]
    public async Task RecordExportAsync_DeletedHighestVersion_ResumesFromRemainingMax()
    {
        var contract = await CreateContractAsync();
        await _service.RecordExportAsync(contract.Id, @"C:\exports\v1.docx", FileType.Docx);
        var second = await _service.RecordExportAsync(contract.Id, @"C:\exports\v2.pdf", FileType.Pdf);

        // Remove the newest record directly; the next export must continue from the max
        // remaining version (not from the row count), keeping versions collision-free.
        _dbContext.GeneratedDocuments.Remove(second);
        await _dbContext.SaveChangesAsync();

        var third = await _service.RecordExportAsync(contract.Id, @"C:\exports\v3.pdf", FileType.Pdf);

        Assert.Equal(2, third.Version);
    }

    [Fact]
    public async Task RecordExportAsync_ContractDeleted_WithCascades()
    {
        var contract = await CreateContractAsync();
        await _service.RecordExportAsync(contract.Id, @"C:\exports\v1.docx", FileType.Docx);
        await _service.RecordExportAsync(contract.Id, @"C:\exports\v2.pdf", FileType.Pdf);

        // Delete the contract row directly: the configured cascade removes the export records.
        var tracked = await _dbContext.Contracts.SingleAsync(c => c.Id == contract.Id);
        _dbContext.Contracts.Remove(tracked);
        await _dbContext.SaveChangesAsync();

        Assert.Empty(await _dbContext.GeneratedDocuments.AsNoTracking()
            .Where(d => d.ContractId == contract.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task RecordExportAsync_ContractDeletedViaService_RemovesExportRecords()
    {
        var contract = await CreateContractAsync();
        await _service.RecordExportAsync(contract.Id, @"C:\exports\v1.docx", FileType.Docx);
        await _service.RecordExportAsync(contract.Id, @"C:\exports\v2.pdf", FileType.Pdf);

        // The application-level delete (ContractService) also purges GeneratedDocuments.
        var contractService = new ContractService(
            _databaseFactory, new FieldService(_databaseFactory), aiService: null);
        await contractService.DeleteAsync(contract.Id);

        Assert.Empty(await _dbContext.GeneratedDocuments.AsNoTracking()
            .Where(d => d.ContractId == contract.Id)
            .ToListAsync());
        Assert.Null(await _dbContext.Contracts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contract.Id));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Seeds a generated-status contract for the given email and returns it.</summary>
    private async Task<Contract> CreateContractAsync(string email = "user@example.com")
    {
        var contract = new Contract
        {
            User = new User
            {
                Email = email,
                DisplayName = "Test User",
                OpenAiApiKeyEncrypted = string.Empty
            },
            Title = "Export NDA",
            ContractType = "Mutual NDA",
            ClientName = "Acme Inc",
            ClientEmail = "legal@acme.example",
            Status = ContractStatus.Generated,
            AssembledText = "Sample contract text."
        };

        _dbContext.Contracts.Add(contract);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        return contract;
    }
}
