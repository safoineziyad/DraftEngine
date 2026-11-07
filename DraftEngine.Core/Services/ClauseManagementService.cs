using System;
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

    public async Task<List<Clause>> GetClausesAsync(string contractType, string? category = null, string? source = null)
    {
        var typeDefinition = ContractTypeRegistry.TryGetByDisplayName(contractType);
        var clauseType = typeDefinition?.Id ?? contractType;
        var legacyType = typeDefinition?.ClauseLookupName;
        var displayName = typeDefinition?.DisplayName;

        using var database = _databaseFactory.CreateContext();
        var query = database.Set<Clause>()
            .AsNoTracking()
            .Where(c => !c.IsArchived && (
                c.ContractType == clauseType ||
                (legacyType != null && c.ContractType == legacyType) ||
                (displayName != null && c.ContractType == displayName)));

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(c => c.Category == category);
        if (!string.IsNullOrWhiteSpace(source) && !string.Equals(source, "All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(c => c.Source == source);

        return await query.OrderBy(c => c.SortOrder).ThenBy(c => c.Title).ToListAsync();
    }

    public Task<List<Clause>> GetUserClausesAsync(string contractType) =>
        GetClausesAsync(contractType, source: "User");

    public async Task<Clause> CreateUserClauseAsync(string contractType, Clause clause)
    {
        using var database = _databaseFactory.CreateContext();
        var created = new Clause
        {
            ContractType = NormalizeContractType(contractType),
            Category = clause.Category,
            Title = clause.Title,
            BodyTemplate = clause.BodyTemplate,
            IsOptional = clause.IsOptional,
            IsDefaultSelected = clause.IsDefaultSelected,
            SortOrder = clause.SortOrder,
            Source = "User",
            OwnerUserId = 1,
            IsArchived = false,
            IsSystem = false,
            IsUserDefined = true
        };
        database.Set<Clause>().Add(created);
        await database.SaveChangesAsync();
        return created;
    }

    public async Task<Clause?> UpdateUserClauseAsync(int clauseId, Clause clause)
    {
        using var database = _databaseFactory.CreateContext();
        var existing = await database.Set<Clause>().FirstOrDefaultAsync(c =>
            c.Id == clauseId && c.Source == "User" && !c.IsArchived);
        if (existing is null) return null;
        existing.Title = clause.Title;
        existing.Category = clause.Category;
        existing.BodyTemplate = clause.BodyTemplate;
        existing.SortOrder = clause.SortOrder;
        existing.IsOptional = clause.IsOptional;
        existing.IsDefaultSelected = clause.IsDefaultSelected;
        await database.SaveChangesAsync();
        return existing;
    }

    public async Task DuplicateClauseAsync(int sourceClauseId, string contractType)
    {
        using var database = _databaseFactory.CreateContext();
        var source = await database.Set<Clause>().FirstOrDefaultAsync(c => c.Id == sourceClauseId && !c.IsArchived);
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
        var clause = await database.Set<Clause>().FirstOrDefaultAsync(c =>
            c.Id == clauseId && c.Source == "User" && !c.IsArchived);
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

    public Task<(bool IsValid, IEnumerable<string> UnknownKeys)> ValidatePlaceholdersAsync(string bodyTemplate)
    {
        if (string.IsNullOrEmpty(bodyTemplate))
            return Task.FromResult((true, (IEnumerable<string>)Enumerable.Empty<string>()));

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Party1","Party2","Party1Address","Party2Address","ContractorName","ContractorAddress",
            "ConsultantName","ConsultantAddress","ClientName","ClientAddress","ClientEmail",
            "EffectiveDate","GoverningLaw","TermMonths","Purpose","ConfidentialInformation","Scope","Services"
        };

        var found = PlaceholderPattern.Matches(bodyTemplate).Cast<Match>().Select(m => m.Groups[1].Value);
        var unknown = found.Where(k => !known.Contains(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return Task.FromResult((unknown.Count == 0, (IEnumerable<string>)unknown));
    }
}
