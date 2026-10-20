using System.Globalization;
using System.Text;
using System.Text.Json;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DraftEngine.Core.Services;

/// <summary>
/// Contract CRUD plus the generation pipeline: syncs the clause selection, fills every selected
/// clause template via <see cref="IFieldService.ReplacePlaceholders"/>, assembles the document
/// text (with an optional AI polish that degrades to template-only output on any failure),
/// persists the result, and applies AI chat-driven edits with friendly error messages.
/// </summary>
public class ContractService : IContractService
{
    /// <summary>Assistant chat reply recorded after a successful chat edit.</summary>
    public const string ChatEditConfirmation = "I've updated the contract based on your instruction.";

    private const string ContractNotFound =
        "This contract could not be found. Open it again from the Contracts list.";

    private const string FriendlyFailure =
        "Something went wrong. Please try again. The full error was written to the log.";

    private readonly IDatabaseContextFactory _databaseFactory;
    private readonly IFieldService _fieldService;
    private readonly IContractAiService? _aiService;
    private readonly ILogger<ContractService>? _logger;

    /// <summary>Creates the contract service.</summary>
    /// <param name="databaseFactory">Factory handing out one short-lived context per operation.</param>
    /// <param name="fieldService">Placeholder mapping used while filling clause templates.</param>
    /// <param name="aiService">
    /// Optional AI service. When null (no API key configured), generation stays template-only
    /// and chat edits report a friendly "add your API key" message.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public ContractService(
        IDatabaseContextFactory databaseFactory,
        IFieldService fieldService,
        IContractAiService? aiService = null,
        ILogger<ContractService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(databaseFactory);
        ArgumentNullException.ThrowIfNull(fieldService);
        _databaseFactory = databaseFactory;
        _fieldService = fieldService;
        _aiService = aiService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Contract> CreateAsync(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        using var database = _databaseFactory.CreateContext();

        var now = DateTime.UtcNow;
        if (contract.CreatedAt == default)
        {
            contract.CreatedAt = now;
        }

        contract.UpdatedAt = now;
        database.Set<Contract>().Add(contract);
        await database.SaveChangesAsync();
        return contract;
    }

    /// <inheritdoc />
    public async Task<Contract?> GetByIdAsync(int id)
    {
        using var database = _databaseFactory.CreateContext();

        return await database.Set<Contract>()
            .Include(contract => contract.ContractClauses).ThenInclude(selection => selection.Clause)
            .Include(contract => contract.ContractFields)
            .AsNoTracking()
            .FirstOrDefaultAsync(contract => contract.Id == id);
    }

    /// <inheritdoc />
    public async Task<List<Contract>> GetAllAsync(int userId)
    {
        using var database = _databaseFactory.CreateContext();

        return await database.Set<Contract>()
            .AsNoTracking()
            .Where(contract => contract.UserId == userId)
            .OrderByDescending(contract => contract.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<Contract?> UpdateAsync(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (contract.Id <= 0)
        {
            return null; // Never persisted: nothing to update.
        }

        using var database = _databaseFactory.CreateContext();

        contract.UpdatedAt = DateTime.UtcNow;
        database.Set<Contract>().Attach(contract);
        database.Entry(contract).State = EntityState.Modified; // only the root row is updated

        try
        {
            await database.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row vanished between load and save: 404-safe null.
            return null;
        }

        return contract;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id)
    {
        using var database = _databaseFactory.CreateContext();

        var contract = await database.Set<Contract>()
            .Include(c => c.ContractClauses)
            .Include(c => c.ContractFields)
            .Include(c => c.ChatMessages)
            .Include(c => c.GeneratedDocuments)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (contract is null)
        {
            return; // Already gone: deleting is idempotent.
        }

        database.Set<ContractClause>().RemoveRange(contract.ContractClauses);
        database.Set<ContractField>().RemoveRange(contract.ContractFields);
        database.Set<ChatMessage>().RemoveRange(contract.ChatMessages);
        database.Set<GeneratedDocument>().RemoveRange(contract.GeneratedDocuments);
        database.Set<Contract>().Remove(contract);

        await database.SaveChangesAsync();
    }

    /// <summary>
    /// Regenerates the contract document. A non-empty <paramref name="selectedClauseIds"/> list
    /// is synced to the stored clause selection first (missing selections are created); an empty
    /// list keeps the stored selection. Each selected clause body is filled from its template
    /// (or custom override) and assembled with a title, preamble, sequential numbering and a
    /// signature block. AI polishing runs only when <paramref name="useAiPolish"/> is true AND an
    /// AI service is configured; any AI failure (missing key, quota, network...) falls back to
    /// the template-only text instead of failing generation. Returns the assembled text.
    /// </summary>
    public async Task<string> GenerateContractAsync(
        int contractId,
        List<int> selectedClauseIds,
        bool useAiPolish = false)
    {
        using var database = _databaseFactory.CreateContext();

        var contract = await database.Set<Contract>()
            .Include(c => c.ContractClauses)
            .FirstOrDefaultAsync(c => c.Id == contractId);

        if (contract is null)
        {
            throw new InvalidOperationException(ContractNotFound);
        }

        if (selectedClauseIds is { Count: > 0 })
        {
            await SyncSelectionAsync(database, contract, selectedClauseIds);
            await database.SaveChangesAsync();
        }

        var selectedClauses = await database.Set<ContractClause>()
            .Include(cc => cc.Clause)
            .Where(cc => cc.ContractId == contractId && cc.Selected)
            .OrderBy(cc => cc.Clause.SortOrder)
            .ToListAsync();

        var fieldValues = await LoadFieldValuesAsync(database, contract);
        var documentText = BuildDocument(contract, selectedClauses, fieldValues);

        if (useAiPolish && _aiService is not null && selectedClauses.Count > 0)
        {
            try
            {
                var tuples = selectedClauses.Select(cc => (ContractClause: cc, Clause: cc.Clause)).ToList();
                var polished = await _aiService.AssembleContractAsync(contract, tuples, fieldValues);
                if (!string.IsNullOrWhiteSpace(polished))
                {
                    documentText = polished.Trim();
                }
            }
            catch (Exception exception)
            {
                // AI polish is best effort: a missing API key, quota or network issue must not
                // break generation, so the deterministic template output is kept instead.
                _logger?.LogWarning(exception, "AI polish failed; using the template-based contract text.");
            }
        }

        contract.AssembledText = documentText;
        contract.LastGeneratedAt = DateTime.UtcNow;
        contract.UpdatedAt = DateTime.UtcNow;
        contract.Status = ContractStatus.Generated;

        await database.SaveChangesAsync();
        return documentText;
    }

    /// <summary>
    /// Applies a natural-language edit through <see cref="IContractAiService"/>. On success the
    /// new text is stored and both the user and assistant chat messages are recorded. On failure
    /// only the user message is recorded and an InvalidOperationException carrying the AI's
    /// friendly error (never raw exception text) is thrown.
    /// </summary>
    public async Task<string> ApplyChatEditAsync(int contractId, string userInstruction)
    {
        if (string.IsNullOrWhiteSpace(userInstruction))
        {
            throw new InvalidOperationException("Describe the change you want first, then send it again.");
        }

        using var database = _databaseFactory.CreateContext();

        var contract = await database.Set<Contract>().FirstOrDefaultAsync(c => c.Id == contractId);
        if (contract is null)
        {
            throw new InvalidOperationException(ContractNotFound);
        }

        if (string.IsNullOrWhiteSpace(contract.AssembledText))
        {
            throw new InvalidOperationException("Generate the contract before asking for edits.");
        }

        if (_aiService is null)
        {
            throw new InvalidOperationException(
                "AI editing is not available. Add your OpenAI API key in Settings and try again.");
        }

        var instruction = userInstruction.Trim();

        ContractEditResult result;
        try
        {
            result = await _aiService.ApplyChatEditAsync(contract, contract.AssembledText, instruction);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "AI chat edit threw; reporting a friendly message.");
            await AppendUserMessageAsync(database, contractId, instruction);
            throw new InvalidOperationException(FriendlyErrorOf(exception));
        }

        if (!result.Success || string.IsNullOrWhiteSpace(result.UpdatedContractText))
        {
            await AppendUserMessageAsync(database, contractId, instruction);
            var reason = string.IsNullOrWhiteSpace(result.ErrorMessage) ? FriendlyFailure : result.ErrorMessage;
            throw new InvalidOperationException(reason);
        }

        contract.AssembledText = result.UpdatedContractText.Trim();
        contract.UpdatedAt = DateTime.UtcNow;

        var now = DateTime.UtcNow;
        database.Set<ChatMessage>().Add(new ChatMessage
        {
            ContractId = contractId,
            Role = MessageRole.User,
            Content = instruction,
            CreatedAt = now,
        });
        database.Set<ChatMessage>().Add(new ChatMessage
        {
            ContractId = contractId,
            Role = MessageRole.Assistant,
            Content = ChatEditConfirmation,
            CreatedAt = now,
        });

        await database.SaveChangesAsync();
        return contract.AssembledText;
    }

    /// <summary>Persists a single user chat message (used on the edit-failure path).</summary>
    private async Task AppendUserMessageAsync(DbContext database, int contractId, string content)
    {
        database.Set<ChatMessage>().Add(new ChatMessage
        {
            ContractId = contractId,
            Role = MessageRole.User,
            Content = content,
            CreatedAt = DateTime.UtcNow,
        });

        await database.SaveChangesAsync();
    }

    /// <summary>
    /// Mirrors the caller's clause selection onto the stored rows: existing rows are toggled and
    /// selections without a row are created. Unknown clause ids are ignored.
    /// </summary>
    private async Task SyncSelectionAsync(DbContext database, Contract contract, List<int> selectedClauseIds)
    {
        var wanted = new HashSet<int>(selectedClauseIds);

        foreach (var selection in contract.ContractClauses)
        {
            selection.Selected = wanted.Contains(selection.ClauseId);
        }

        var known = contract.ContractClauses.Select(selection => selection.ClauseId).ToHashSet();
        foreach (var clauseId in wanted)
        {
            if (known.Contains(clauseId))
            {
                continue;
            }

            if (!await database.Set<Clause>().AnyAsync(clause => clause.Id == clauseId))
            {
                continue;
            }

            database.Set<ContractClause>().Add(new ContractClause
            {
                ContractId = contract.Id,
                ClauseId = clauseId,
                Selected = true,
            });
        }
    }

    /// <summary>
    /// Builds the dictionary used for placeholder substitution: contract metadata and parties are
    /// seeded first, then stored intake fields overlay them (explicit answers win).
    /// </summary>
    private async Task<Dictionary<string, string>> LoadFieldValuesAsync(DbContext database, Contract contract)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(contract.ClientName))
        {
            values["ClientName"] = contract.ClientName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(contract.ClientEmail))
        {
            values["ClientEmail"] = contract.ClientEmail.Trim();
        }

        if (!string.IsNullOrWhiteSpace(contract.GoverningLaw))
        {
            values["GoverningLaw"] = contract.GoverningLaw.Trim();
        }

        if (contract.TermMonths > 0)
        {
            values["TermMonths"] = contract.TermMonths.ToString(CultureInfo.InvariantCulture);
        }

        if (contract.EffectiveDate.HasValue)
        {
            // Raw ISO form; FieldService formats it as a long-form date on substitution.
            values["EffectiveDate"] = contract.EffectiveDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        AddPartyValues(contract.PartiesJson, values);

        if (!values.ContainsKey("Party1") && !string.IsNullOrWhiteSpace(contract.ClientName))
        {
            values["Party1"] = contract.ClientName.Trim();
        }

        var storedFields = await database.Set<ContractField>()
            .AsNoTracking()
            .Where(field => field.ContractId == contract.Id)
            .ToListAsync();

        foreach (var field in storedFields)
        {
            if (!string.IsNullOrWhiteSpace(field.Key))
            {
                values[field.Key.Trim()] = field.Value ?? string.Empty;
            }
        }

        return values;
    }

    /// <summary>
    /// Extracts up to two party names and addresses from the contract's PartiesJson
    /// (array of strings or of objects with name/address properties) into placeholder values.
    /// </summary>
    private static void AddPartyValues(string? partiesJson, Dictionary<string, string> values)
    {
        if (string.IsNullOrWhiteSpace(partiesJson))
        {
            return;
        }

        var parties = new List<(string Name, string Address)>();

        try
        {
            using var document = JsonDocument.Parse(partiesJson);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var text = element.GetString();
                        parties.Add((string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim(), string.Empty));
                        continue;
                    }

                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var name = string.Empty;
                    var address = string.Empty;
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var text = property.Value.GetString();
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        if (property.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                        {
                            name = string.IsNullOrEmpty(name) ? text.Trim() : name;
                        }
                        else if (property.Name.Equals("address", StringComparison.OrdinalIgnoreCase))
                        {
                            address = string.IsNullOrEmpty(address) ? text.Trim() : address;
                        }
                    }

                    parties.Add((name, address));
                }
            }
        }
        catch (JsonException)
        {
            // Not valid JSON: fall back to the raw text as the first party name,
            // mirroring how the document exporters read PartiesJson.
            parties.Add((partiesJson.Trim(), string.Empty));
        }

        if (parties.Count > 0 && !string.IsNullOrEmpty(parties[0].Name))
        {
            values["Party1"] = parties[0].Name;
        }

        if (parties.Count > 1 && !string.IsNullOrEmpty(parties[1].Name))
        {
            values["Party2"] = parties[1].Name;
        }

        if (parties.Count > 0 && !string.IsNullOrEmpty(parties[0].Address))
        {
            values["Party1Address"] = parties[0].Address;
        }

        if (parties.Count > 1 && !string.IsNullOrEmpty(parties[1].Address))
        {
            values["Party2Address"] = parties[1].Address;
        }
    }

    /// <summary>
    /// Assembles the document as plain text: title line, party/effective-date preamble, each
    /// selected clause as "N. Title" plus its filled body (blank line between clauses), then a
    /// signature block. Numbering is sequential from 1 and heading lines match the
    /// "N. " pattern the DOCX/PDF builders detect.
    /// </summary>
    private string BuildDocument(
        Contract contract,
        IReadOnlyList<ContractClause> selectedClauses,
        Dictionary<string, string> fieldValues)
    {
        var builder = new StringBuilder();

        builder.AppendLine(string.IsNullOrWhiteSpace(contract.Title) ? "Contract" : contract.Title.Trim());
        builder.AppendLine();
        builder.AppendLine(_fieldService.ReplacePlaceholders(BuildPreamble(contract), fieldValues));
        builder.AppendLine();

        var number = 1;
        foreach (var selection in selectedClauses)
        {
            var template = !string.IsNullOrWhiteSpace(selection.CustomBodyOverride)
                ? selection.CustomBodyOverride
                : selection.Clause.BodyTemplate;

            var heading = string.IsNullOrWhiteSpace(selection.Clause.Title) ? "Clause" : selection.Clause.Title.Trim();
            var body = _fieldService.ReplacePlaceholders(template, fieldValues).Trim();

            builder.AppendLine(CultureInfo.InvariantCulture, $"{number}. {heading}");
            builder.AppendLine(body);
            builder.AppendLine();
            number++;
        }

        builder.AppendLine("IN WITNESS WHEREOF, the parties have executed this Agreement as of the Effective Date.");
        builder.AppendLine();
        builder.AppendLine(_fieldService.ReplacePlaceholders(
            "{{Party1}}: ______________________________    Date: ____________________", fieldValues));
        builder.AppendLine();
        builder.AppendLine(_fieldService.ReplacePlaceholders(
            "{{Party2}}: ______________________________    Date: ____________________", fieldValues));

        return builder.ToString();
    }

    /// <summary>Preamble template identifying the contract type, effective date and both parties.</summary>
    private static string BuildPreamble(Contract contract)
    {
        var typeLabel = string.IsNullOrWhiteSpace(contract.ContractType)
            ? "Agreement"
            : contract.ContractType.Trim();

        return "This " + typeLabel +
               " is entered into as of {{EffectiveDate}} by and between {{Party1}} and {{Party2}}, " +
               "and sets out the terms under which the parties will exchange and protect confidential information.";
    }

    /// <summary>
    /// Maps an exception to a user-safe sentence: the AI layer's FriendlyMessage when present
    /// (never its technical message), friendly InvalidOperationException messages as-is,
    /// otherwise a generic sentence.
    /// </summary>
    private static string FriendlyErrorOf(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var friendlyProperty = current.GetType().GetProperty("FriendlyMessage");
            if (friendlyProperty?.PropertyType == typeof(string) &&
                friendlyProperty.GetValue(current) is string friendly &&
                !string.IsNullOrWhiteSpace(friendly))
            {
                return friendly;
            }

            if (current is InvalidOperationException && !string.IsNullOrWhiteSpace(current.Message))
            {
                return current.Message;
            }
        }

        return FriendlyFailure;
    }
}
