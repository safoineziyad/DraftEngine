using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Models.ContractTypes;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Core.Services;

/// <summary>
/// Reads clause templates for a contract type from the database, ordered by
/// <see cref="Clause.SortOrder"/>. Queries are no-tracking: clauses are shared
/// reference data and never modified through this service. Stateless: each
/// operation opens its own short-lived context via <see cref="IDatabaseContextFactory"/>.
/// <para>
/// Contract types are canonicalised through <see cref="ContractTypeRegistry"/>, so callers may
/// pass either the canonical id (<c>MutualNda</c>) or the display name (<c>Mutual NDA</c>).
/// Archived clauses and clauses belonging to an unknown type never appear in the wizard.
/// </para>
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
        var definition = ContractTypeRegistry.TryGetByDisplayName(contractType);
        if (definition is null)
        {
            return new List<Clause>();
        }

        using var database = _databaseFactory.CreateContext();

        return await MatchingType(database.Set<Clause>(), definition)
            .Where(clause => !clause.IsArchived)
            .OrderBy(clause => clause.SortOrder)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<Clause>> GetDefaultSelectedClausesAsync(string contractType)
    {
        var definition = ContractTypeRegistry.TryGetByDisplayName(contractType);
        if (definition is null)
        {
            return new List<Clause>();
        }

        using var database = _databaseFactory.CreateContext();

        return await MatchingType(database.Set<Clause>(), definition)
            .Where(clause => !clause.IsArchived && clause.IsDefaultSelected)
            .OrderBy(clause => clause.SortOrder)
            .ToListAsync();
    }

    /// <summary>
    /// Matches both the canonical id and the legacy display/lookup names so rows written by
    /// older seed migrations or older user clauses are still found after the id migration.
    /// </summary>
    private static IQueryable<Clause> MatchingType(
        IQueryable<Clause> clauses,
        ContractTypeDefinition definition)
    {
        var legacyName = string.IsNullOrWhiteSpace(definition.ClauseLookupName)
            ? definition.DisplayName
            : definition.ClauseLookupName;

        return clauses.Where(clause =>
            clause.ContractType == definition.Id ||
            clause.ContractType == legacyName ||
            clause.ContractType == definition.DisplayName);
    }
}
