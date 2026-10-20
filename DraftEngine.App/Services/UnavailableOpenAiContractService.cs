using DraftEngine.AI.Exceptions;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;

namespace DraftEngine.App.Services;

/// <summary>
/// User-safe stand-in for <see cref="IContractAiService"/> used when no OpenAI API key is
/// configured. It never contacts the network; every drafting call throws the typed
/// <see cref="OpenAiApiKeyMissingException"/> so the UI can guide the user to Settings,
/// while the connectivity checks report a friendly failure instead of throwing.
/// This keeps application startup (and every non-AI feature) working without a key.
/// </summary>
public sealed class UnavailableOpenAiContractService : IContractAiService
{
    private static readonly string MissingKeyMessage = new OpenAiApiKeyMissingException().FriendlyMessage;

    /// <inheritdoc />
    public Task<string> GenerateClauseBodyAsync(Clause clause, Dictionary<string, string> fieldValues)
        => throw new OpenAiApiKeyMissingException();

    /// <inheritdoc />
    public Task<string> AssembleContractAsync(Contract contract, List<ContractClause> clauses, Dictionary<string, string> fieldValues)
        => throw new OpenAiApiKeyMissingException();

    /// <inheritdoc />
    public Task<string> AssembleContractAsync(Contract contract, List<(ContractClause ContractClause, Clause Clause)> clauses, Dictionary<string, string> fieldValues)
        => throw new OpenAiApiKeyMissingException();

    /// <inheritdoc />
    public Task<ContractEditResult> ApplyChatEditAsync(Contract contract, string currentDocument, string userInstruction)
        => throw new OpenAiApiKeyMissingException();

    /// <inheritdoc />
    public Task<bool> TestConnectionAsync() => Task.FromResult(false);

    /// <inheritdoc />
    public Task<OpenAiConnectionTestResult> TestConnectionDetailedAsync()
        => Task.FromResult(new OpenAiConnectionTestResult { Success = false, Message = MissingKeyMessage });
}
