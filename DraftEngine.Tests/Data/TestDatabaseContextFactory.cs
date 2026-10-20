using DraftEngine.Core.Interfaces;
using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Test double for <see cref="IDatabaseContextFactory"/>: hands out a fresh
/// <see cref="DraftEngineDbContext"/> per call over the test's shared in-memory SQLite
/// connection, mirroring exactly how the application behaves in production.
/// </summary>
public sealed class TestDatabaseContextFactory : IDatabaseContextFactory
{
    private readonly DbContextOptions<DraftEngineDbContext> _options;

    /// <summary>Creates the factory over the given context options.</summary>
    public TestDatabaseContextFactory(DbContextOptions<DraftEngineDbContext> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public DbContext CreateContext() => new DraftEngineDbContext(_options);
}
