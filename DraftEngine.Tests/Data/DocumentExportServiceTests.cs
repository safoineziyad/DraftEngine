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

        _service = new DocumentExportService(_dbContext);
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

    // ---------------------------------------------------------------- helpers

    private async Task<Contract> CreateContractAsync()
    {
        var contract = new Contract
        {
            User = new User
            {
                Email = "user@example.com",
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
