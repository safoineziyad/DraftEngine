using System.Collections.Generic;
using System.Threading.Tasks;
using DraftEngine.Core.Models;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Service for managing clauses.
/// </summary>
public interface IClauseService
{
    Task<List<Clause>> GetClausesByContractTypeAsync(string contractType);
    Task<List<Clause>> GetDefaultSelectedClausesAsync(string contractType);
}
