using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Core.Services;

/// <summary>
/// Reads clause templates for a contract type from the database, ordered by
/// <see cref="Clause.SortOrder"/>. Queries are no-tracking: clauses are shared
/// reference data and never modified through this service.
/// </summary>
public class ClauseService : IClauseService
{
    private readonly DbContext _database;

    /// <summary>Creates the clause service.</summary>
    /// <param name="database">EF Core context hosting the DraftEngine model.</param>
    public ClauseService(DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public async Task<List<Clause>> GetClausesByContractTypeAsync(string contractType) =>
        await _database.Set<Clause>()
            .AsNoTracking()
            .Where(clause => clause.ContractType == contractType)
            .OrderBy(clause => clause.SortOrder)
            .ToListAsync();

    /// <inheritdoc />
    public async Task<List<Clause>> GetDefaultSelectedClausesAsync(string contractType) =>
        await _database.Set<Clause>()
            .AsNoTracking()
            .Where(clause => clause.ContractType == contractType && clause.IsDefaultSelected)
            .OrderBy(clause => clause.SortOrder)
            .ToListAsync();
}
