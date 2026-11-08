using System.Collections.Generic;
using System.Threading.Tasks;
using DraftEngine.Core.Models;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Manages user-owned clause templates: create, edit, duplicate, soft-archive.
/// Seeded (system) clauses are read-only but can be duplicated into user clauses.
/// </summary>
public interface IClauseManagementService
{
    Task<List<Clause>> GetClausesAsync(string contractType, string? category = null, string? source = null);
    Task<List<Clause>> GetUserClausesAsync(string contractType);
    Task<Clause> CreateUserClauseAsync(string contractType, Clause clause);
    Task<Clause?> UpdateUserClauseAsync(int clauseId, Clause clause);
    Task DuplicateClauseAsync(int sourceClauseId, string contractType);
    Task ArchiveUserClauseAsync(int clauseId);
    Task<(bool IsValid, IEnumerable<string> UnknownKeys)> ValidatePlaceholdersAsync(string bodyTemplate);
}
