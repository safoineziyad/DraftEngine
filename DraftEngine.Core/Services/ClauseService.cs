using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Core.Services;

/// <summary>
/// Reads clause templates for a contract type from the database, ordered by
/// <see cref="Clause.SortOrder"/>. Queries are no-tracking: clauses are shared
/// reference data and never modified through this service. Stateless: each
/// operation opens its own short-lived context via <see cref="IDatabaseContextFactory"/>.
/// </summary>
public class ClauseService : IClauseService
{
    private readonly IDatabaseContextFactory _databaseFactory;

    /// <summary>Creates the clause service.</summary>
    /// <param name="databaseFactory">Factory handing out one short-lived context per operation.</param>
    public ClauseService(IDatabaseContextFactory databaseFactory)
    {
        ArgumentNullException.ThrowIfNull(databaseFactory);
        _databaseFactory = databaseFactory;
    }

    /// <inheritdoc />
    public async Task<List<Clause>> GetClausesByContractTypeAsync(string contractType)
    {
        using var database = _databaseFactory.CreateContext();

        return await database.Set<Clause>()
            .AsNoTracking()
            .Where(clause => clause.ContractType == contractType)
            .OrderBy(clause => clause.SortOrder)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<Clause>> GetDefaultSelectedClausesAsync(string contractType)
    {
        using var database = _databaseFactory.CreateContext();

        return await database.Set<Clause>()
            .AsNoTracking()
            .Where(clause => clause.ContractType == contractType && clause.IsDefaultSelected)
            .OrderBy(clause => clause.SortOrder)
            .ToListAsync();
    }
}
