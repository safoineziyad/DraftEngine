using DraftEngine.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Data;

/// <summary>
/// EF Core adapter over <see cref="IDbContextFactory{DraftEngineDbContext}"/> implementing the
/// Core-layer <see cref="IDatabaseContextFactory"/> abstraction (DraftEngine.Core cannot
/// reference DraftEngine.Data, so the concrete factory is bridged here). Each call hands out a
/// brand-new, unshared <see cref="DraftEngineDbContext"/>.
/// </summary>
public sealed class DatabaseContextFactory : IDatabaseContextFactory
{
    private readonly IDbContextFactory<DraftEngineDbContext> _contextFactory;

    /// <summary>Creates the adapter.</summary>
    /// <param name="contextFactory">The EF Core context factory registered by AddDbContextFactory.</param>
    public DatabaseContextFactory(IDbContextFactory<DraftEngineDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public DbContext CreateContext() => _contextFactory.CreateDbContext();
}
