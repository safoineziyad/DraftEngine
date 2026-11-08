using DraftEngine.App.Services;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

public sealed class ExportHistoryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DraftEngineDbContext _dbContext;
    private readonly ExportHistoryService _service;

    public ExportHistoryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(options);
        _dbContext.Database.EnsureCreated();
        _service = new ExportHistoryService(new TestDatabaseContextFactory(options));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsContractExportMetadataNewestFirst()
    {
        var contract = new Contract { UserId = 1, Title = "History contract" };
        var otherContract = new Contract { UserId = 1, Title = "Other contract" };
        _dbContext.Contracts.AddRange(contract, otherContract);
        await _dbContext.SaveChangesAsync();

        var older = new GeneratedDocument
        {
            ContractId = contract.Id,
            FileType = FileType.Docx,
            GeneratedAt = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            Version = 1,
            FilePath = @"C:\exports\older.docx"
        };
        var newer = new GeneratedDocument
        {
            ContractId = contract.Id,
            FileType = FileType.Pdf,
            GeneratedAt = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
            Version = 3,
            FilePath = @"C:\exports\newer.pdf"
        };
        var unrelated = new GeneratedDocument
        {
            ContractId = otherContract.Id,
            FileType = FileType.Pdf,
            GeneratedAt = new DateTime(2026, 12, 1, 10, 0, 0, DateTimeKind.Utc),
            Version = 2,
            FilePath = @"C:\exports\unrelated.pdf"
        };
        _dbContext.GeneratedDocuments.AddRange(older, newer, unrelated);
        await _dbContext.SaveChangesAsync();

        var history = await _service.GetHistoryAsync(contract.Id);

        Assert.Collection(history,
            document =>
            {
                Assert.Equal(newer.Id, document.Id);
                Assert.Equal(FileType.Pdf, document.FileType);
                Assert.Equal(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), document.GeneratedAt);
                Assert.Equal(3, document.Version);
                Assert.Equal(@"C:\exports\newer.pdf", document.FilePath);
            },
            document =>
            {
                Assert.Equal(older.Id, document.Id);
                Assert.Equal(FileType.Docx, document.FileType);
                Assert.Equal(new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), document.GeneratedAt);
                Assert.Equal(1, document.Version);
                Assert.Equal(@"C:\exports\older.docx", document.FilePath);
            });
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsEmptyListWhenContractHasNoExports()
    {
        var history = await _service.GetHistoryAsync(12345);

        Assert.Empty(history);
    }

    [Fact]
    public void GetFileStatus_ReportsAvailableMissingAndInvalidPaths()
    {
        Assert.Equal(
            ExportFileStatus.Available,
            ExportHistoryService.GetFileStatus(typeof(ExportHistoryServiceTests).Assembly.Location));
        Assert.Equal(
            ExportFileStatus.Missing,
            ExportHistoryService.GetFileStatus(Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.pdf")));
        Assert.Equal(ExportFileStatus.InvalidPath, ExportHistoryService.GetFileStatus(null));
        Assert.Equal(ExportFileStatus.InvalidPath, ExportHistoryService.GetFileStatus(string.Empty));
        Assert.Equal(
            ExportFileStatus.InvalidPath,
            ExportHistoryService.GetFileStatus(AppContext.BaseDirectory));
    }
}
