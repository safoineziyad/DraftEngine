using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Models.ContractTypes;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Core.Services;

/// <summary>
/// Manages user-owned clause templates. User clauses are stored in the Clauses
/// table with Source = "User" and OwnerUserId = 1 (single-user assumption).
/// System clauses (Source = "System") are read-only but duplicable.
/// </summary>
public class ClauseManagementService : IClauseManagementService
{
    private readonly IDatabaseContextFactory _databaseFactory;
    private static readonly Regex PlaceholderPattern = new(@"\{\{([\w\-.]+)\}\}", RegexOptions.Compiled);

    public ClauseManagementService(IDatabaseContextFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public async Task<List<Clause>> GetUserClausesAsync(string contractType)
    {
        using var database = _databaseFactory.CreateContext();
        return await database.Set<Clause>()
            .AsNoTracking()
            .Where(c => c.ContractType == contractType && c.Source == "User" && !c.IsArchived)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<Clause> CreateUserClauseAsync(string contractType, Clause clause)
    {
        using var database = _databaseFactory.CreateContext();
        clause.ContractType = NormalizeContractType(contractType);
        clause.Source = "User";
        clause.OwnerUserId = 1;
        clause.IsArchived = false;
        clause.IsSystem = false;
        clause.IsUserDefined = true;
        clause.ContractClauses = null;
        database.Set<Clause>().Add(clause);
        await database.SaveChangesAsync();
        return clause;
    }

    public async Task<Clause> UpdateUserClauseAsync(int clauseId, Clause clause)
    {
        using var database = _databaseFactory.CreateContext();
        var existing = await database.Set<Clause>().FirstOrDefaultAsync(c => c.Id == clauseId && c.Source == "User");
        if (existing is null) return null;
        existing.Title = clause.Title;
        existing.Category = clause.Category;
        existing.BodyTemplate = clause.BodyTemplate;
        existing.SortOrder = clause.SortOrder;
        existing.IsOptional = clause.IsOptional;
        await database.SaveChangesAsync();
        return existing;
    }

    public async Task DuplicateClauseAsync(int sourceClauseId, string contractType)
    {
        using var database = _databaseFactory.CreateContext();
        var source = await database.Set<Clause>().FirstOrDefaultAsync(c => c.Id == sourceClauseId);
        if (source is null) return;
        var copy = new Clause
        {
            ContractType = NormalizeContractType(contractType),
            Category = source.Category,
            Title = $"Copy of {source.Title}",
            BodyTemplate = source.BodyTemplate,
            IsOptional = source.IsOptional,
            IsDefaultSelected = false,
            SortOrder = source.SortOrder + 1,
            Source = "User",
            OwnerUserId = 1,
            IsArchived = false,
            IsSystem = false,
            IsUserDefined = true
        };
        database.Set<Clause>().Add(copy);
        await database.SaveChangesAsync();
    }

    public async Task ArchiveUserClauseAsync(int clauseId)
    {
        using var database = _databaseFactory.CreateContext();
        var clause = await database.Set<Clause>().FirstOrDefaultAsync(c => c.Id == clauseId && c.Source == "User");
        if (clause is null) return;
        clause.IsArchived = true;
        await database.SaveChangesAsync();
    }

    /// <summary>
    /// Stores the canonical registry id when the caller supplied a display name or alias, so
    /// clause rows written by different parts of the app agree on one spelling.
    /// </summary>
    private static string NormalizeContractType(string contractType) =>
        ContractTypeRegistry.TryGetByDisplayName(contractType)?.Id ?? contractType;

    public async Task<(bool IsValid, IEnumerable<string> UnknownKeys)> ValidatePlaceholdersAsync(string bodyTemplate)
    {
        if (string.IsNullOrEmpty(bodyTemplate)) return (true, Enumerable.Empty<string>());

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Party1","Party2","Party1Address","Party2Address","ContractorName","ContractorAddress",
            "ConsultantName","ConsultantAddress","ClientName","ClientAddress","ClientEmail",
            "EffectiveDate","GoverningLaw","TermMonths","Purpose","ConfidentialInformation","Scope","Services"
        };

        var found = PlaceholderPattern.Matches(bodyTemplate).Cast<Match>().Select(m => m.Groups[1].Value);
        var unknown = found.Where(k => !known.Contains(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return (unknown.Count == 0, unknown);
    }
}
