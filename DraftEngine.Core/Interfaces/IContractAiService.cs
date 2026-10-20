using System.Collections.Generic;
using System.Threading.Tasks;
using DraftEngine.Core.Models;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// AI service for contract generation and editing. Implementations throw typed exceptions
/// carrying user-safe messages on failure.
/// </summary>
public interface IContractAiService
{
    /// <summary>Fills a single clause template with the provided field values.</summary>
    Task<string> GenerateClauseBodyAsync(Clause clause, Dictionary<string, string> fieldValues);

    /// <summary>
    /// AI-assisted polishing of already-filled clause text. Only selected clauses carrying a
    /// non-empty <see cref="ContractClause.CustomBodyOverride"/> are used; the rest are skipped
    /// because template/placeholder filling belongs to the caller (domain layer), not to the AI layer.
    /// Throws <see cref="System.InvalidOperationException"/> when no selected clause carries text.
    /// </summary>
    Task<string> AssembleContractAsync(Contract contract, List<ContractClause> clauses, Dictionary<string, string> fieldValues);

    /// <summary>
    /// AI-assisted polishing of already-filled clause text. For each selected clause the
    /// <see cref="ContractClause.CustomBodyOverride"/> is used when present, otherwise the
    /// (already filled) <see cref="Clause.BodyTemplate"/> text is used; clauses with no usable
    /// text are skipped. Templates must be filled with the intake field values by the caller
    /// before this call.
    /// Throws <see cref="System.InvalidOperationException"/> when no selected clause carries text.
    /// </summary>
    Task<string> AssembleContractAsync(Contract contract, List<(ContractClause ContractClause, Clause Clause)> clauses, Dictionary<string, string> fieldValues);

    /// <summary>Applies a natural-language edit to the current contract text.</summary>
    Task<ContractEditResult> ApplyChatEditAsync(Contract contract, string currentDocument, string userInstruction);

    /// <summary>Quick connectivity check against the AI provider; false on any failure.</summary>
    Task<bool> TestConnectionAsync();

    /// <summary>Connectivity check against the AI provider including a user-safe failure reason.</summary>
    Task<OpenAiConnectionTestResult> TestConnectionDetailedAsync();
}

/// <summary>
/// Result of a chat-based contract edit.
/// </summary>
public class ContractEditResult
{
    public bool Success { get; set; }
    public string UpdatedContractText { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Result of an AI connectivity test, with a user-safe message describing success or failure.
/// Lives in Core (not in the AI project) because this interface cannot reference DraftEngine.AI.
/// </summary>
public class OpenAiConnectionTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
