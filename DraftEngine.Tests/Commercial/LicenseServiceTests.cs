using DraftEngine.App.Configuration;
using DraftEngine.App.Services.Licensing;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Licensing;
using DraftEngine.Data;
using DraftEngine.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Commercial;

/// <summary>
/// Tests for <see cref="LicenseService" />: when <c>Features:LicensingEnabled</c> is off the
/// service reports itself as disabled and does not touch the license file.
/// </summary>
public class LicenseServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DraftEngineDbContext _dbContext;
    private readonly TestDatabaseContextFactory _factory;
    private readonly LicenseService _service;

    public LicenseServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(options);
        _dbContext.Database.EnsureCreated();
        _factory = new TestDatabaseContextFactory(options);

        var appOptions = new AppOptions { Features = { LicensingEnabled = false } };
        _service = new LicenseService(
            appOptions,
            new FakeSecretProtector(),
            new FakeLicenseVerifier(),
            _factory);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task LoadAsync_LicensingDisabled_ReturnsDisabled()
    {
        Assert.False(_service.IsEnabled);

        var evaluation = await _service.LoadAsync();
        Assert.Equal(LicenseStatus.Disabled, evaluation.Status);
        Assert.False(evaluation.IsUsable);
    }

    private sealed class FakeSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => "protected:" + plaintext;
        public string? Unprotect(string protectedValue) => null;
    }

    private sealed class FakeLicenseVerifier : DraftEngine.App.Services.Licensing.ILicenseVerifier
    {
        public DraftEngine.Core.Licensing.LicenseEvaluation Verify(
            string? licenseKey,
            DateTimeOffset now,
            TimeSpan? gracePeriod = null,
            DateTimeOffset? lastVerifiedUtc = null) =>
            new DraftEngine.Core.Licensing.LicenseEvaluation(
                DraftEngine.Core.Licensing.LicenseStatus.Valid,
                null,
                "valid");
    }
}
