using DraftEngine.AI.Exceptions;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;

namespace DraftEngine.App.Services;

/// <summary>
/// <see cref="IContractAiService"/> that resolves the real OpenAI client on every call from the
/// key saved in Settings (falling back to the appsettings/environment key). Saving a new key in
/// Settings therefore takes effect immediately, without restarting the app. When no key exists
/// anywhere, drafting calls throw <see cref="OpenAiApiKeyMissingException"/> so the UI can guide
/// the user to Settings.
/// </summary>
public sealed class SettingsBackedAiService : IContractAiService
{
    private readonly IAppSettingsService _settings;
    private readonly AppOptions _options;
    private readonly object _gate = new();
    private IContractAiService? _cached;
    private string _cachedKey = string.Empty;
    private string _cachedModel = string.Empty;

    /// <summary>Creates the settings-backed AI service.</summary>
    public SettingsBackedAiService(IAppSettingsService settings, AppOptions options)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private IContractAiService? Resolve()
    {
        var current = _settings.Current;
        var key = !string.IsNullOrWhiteSpace(current.OpenAiApiKey)
            ? current.OpenAiApiKey.Trim()
            : _options.OpenAi.ApiKey?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            return null;
        }

        var model = !string.IsNullOrWhiteSpace(current.OpenAiApiKey)
            ? current.OpenAiModel
            : _options.OpenAi.Model;

        lock (_gate)
        {
            if (_cached is null || _cachedKey != key || _cachedModel != model)
            {
                _cached = _settings.CreateAiService(key, model);
                _cachedKey = key;
                _cachedModel = model;
            }

            return _cached;
        }
    }

    private IContractAiService Require() => Resolve() ?? throw new OpenAiApiKeyMissingException();

    /// <inheritdoc />
    public Task<string> GenerateClauseBodyAsync(Clause clause, Dictionary<string, string> fieldValues)
        => Require().GenerateClauseBodyAsync(clause, fieldValues);

    /// <inheritdoc />
    public Task<string> AssembleContractAsync(Contract contract, List<ContractClause> clauses, Dictionary<string, string> fieldValues)
        => Require().AssembleContractAsync(contract, clauses, fieldValues);

    /// <inheritdoc />
    public Task<string> AssembleContractAsync(Contract contract, List<(ContractClause ContractClause, Clause Clause)> clauses, Dictionary<string, string> fieldValues)
        => Require().AssembleContractAsync(contract, clauses, fieldValues);

    /// <inheritdoc />
    public Task<ContractEditResult> ApplyChatEditAsync(Contract contract, string currentDocument, string userInstruction)
        => Require().ApplyChatEditAsync(contract, currentDocument, userInstruction);

    /// <inheritdoc />
    public Task<bool> TestConnectionAsync()
        => Resolve()?.TestConnectionAsync() ?? Task.FromResult(false);

    /// <inheritdoc />
    public Task<OpenAiConnectionTestResult> TestConnectionDetailedAsync()
        => Resolve()?.TestConnectionDetailedAsync()
           ?? Task.FromResult(new OpenAiConnectionTestResult
           {
               Success = false,
               Message = new OpenAiApiKeyMissingException().FriendlyMessage,
           });
}
