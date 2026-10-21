using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    /// <summary>Snapshot reason recorded before an AI chat edit replaces the text.</summary>
    public const string AiEditReason = "AI edit";

    /// <summary>Snapshot reason recorded before a regeneration replaces the text.</summary>
    public const string RegenerationReason = "Regeneration";

    /// <summary>Snapshot reason recorded before a revert replaces the text.</summary>
    public const string RevertReason = "Before revert";

    // Version history hygiene: snapshots are taken automatically, so cap them per contract
    // to keep the database (and the history list) bounded for long-lived contracts.
    private const int MaxVersionsPerContract = 50;

    // Chat history hygiene (audit Finding #7): every chat edit appends two messages, so an
    // active contract would otherwise grow the table without bound. Only the newest
    // messages matter for the on-screen history.
    private const int MaxChatMessagesPerContract = 200;

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
            .Include(c => c.Versions)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (contract is null)
        {
            return; // Already gone: deleting is idempotent.
        }

        database.Set<ContractClause>().RemoveRange(contract.ContractClauses);
        database.Set<ContractField>().RemoveRange(contract.ContractFields);
        database.Set<ChatMessage>().RemoveRange(contract.ChatMessages);
        database.Set<GeneratedDocument>().RemoveRange(contract.GeneratedDocuments);
        database.Set<ContractVersion>().RemoveRange(contract.Versions);
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

        if (documentText.Contains("{{", StringComparison.Ordinal))
        {
            // Audit Finding #11: never let an unfilled placeholder pass silently — the UI
            // surfaces a friendly hint on top of this log entry.
            _logger?.LogWarning(
                "Contract {ContractId} was generated with unresolved placeholders still visible as {{...}}.",
                contract.Id);
        }

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

        // Keep the text being replaced in the version history so a regeneration is undoable.
        StageVersionSnapshot(database, contract, RegenerationReason);

        contract.AssembledText = documentText;
        contract.LastGeneratedAt = DateTime.UtcNow;
        contract.UpdatedAt = DateTime.UtcNow;
        contract.Status = ContractStatus.Generated;

        await database.SaveChangesAsync();
        await PruneOldVersionsAsync(database, contract.Id);
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

        var updatedText = result.UpdatedContractText.Trim();

        // Guard against a truncated or clause-dropping reply before it can replace the
        // stored contract (audit Finding #2): an accepted edit must not silently destroy
        // most of the document.
        var validationError = ValidateEditedText(contract.AssembledText, updatedText);
        if (validationError is not null)
        {
            await AppendUserMessageAsync(database, contractId, instruction);
            throw new InvalidOperationException(validationError);
        }

        // Keep the pre-edit text in the version history so the user can always revert a
        // chat edit that damaged the contract (audit Finding #1).
        StageVersionSnapshot(database, contract, AiEditReason);

        contract.AssembledText = updatedText;
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
        await PruneOldVersionsAsync(database, contractId);
        await PruneOldChatMessagesAsync(database, contractId);
        return contract.AssembledText;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContractVersion>> GetVersionsAsync(int contractId)
    {
        using var database = _databaseFactory.CreateContext();

        return await database.Set<ContractVersion>()
            .AsNoTracking()
            .Where(version => version.ContractId == contractId)
            .OrderBy(version => version.VersionNumber)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<string> RevertToVersionAsync(int contractId, int versionId)
    {
        using var database = _databaseFactory.CreateContext();

        var contract = await database.Set<Contract>()
            .FirstOrDefaultAsync(c => c.Id == contractId);
        if (contract is null)
        {
            throw new InvalidOperationException(ContractNotFound);
        }

        var version = await database.Set<ContractVersion>()
            .FirstOrDefaultAsync(v => v.Id == versionId && v.ContractId == contractId);
        if (version is null)
        {
            throw new InvalidOperationException(
                "That earlier version could not be found. Reload the version history and try again.");
        }

        if (string.IsNullOrWhiteSpace(contract.AssembledText))
        {
            throw new InvalidOperationException("There is no current document to revert.");
        }

        if (string.Equals(contract.AssembledText, version.Text, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The contract already shows this version, so there is nothing to restore.");
        }

        // Snapshot the text being replaced: reverting must itself be undoable.
        StageVersionSnapshot(database, contract, RevertReason);

        contract.AssembledText = version.Text;
        contract.UpdatedAt = DateTime.UtcNow;

        await database.SaveChangesAsync();
        await PruneOldVersionsAsync(database, contractId);
        return contract.AssembledText;
    }

    /// <summary>Matches the numbered clause headings ("1. Title") the document builders detect.</summary>
    private static readonly Regex ClauseHeadingPattern = new(@"^\s*\d+\.\s+\S", RegexOptions.Compiled);

    /// <summary>
    /// Rejects an AI reply that would clearly destroy the contract (audit Finding #2): text
    /// below a third of the original length is treated as truncated, and losing more than one
    /// numbered clause heading is treated as accidental clause deletion. Returns a
    /// user-friendly rejection message, or null when the reply looks sane. Legitimate edits
    /// (wording changes, condensing a single clause) always pass.
    /// </summary>
    internal static string? ValidateEditedText(string originalText, string editedText)
    {
        if (editedText.Length * 3 < originalText.Length)
        {
            return "The assistant's reply looks incomplete — it is much shorter than the current " +
                   "contract — so it was not applied. Nothing was changed. Try a more specific request.";
        }

        var originalHeadings = CountClauseHeadings(originalText);
        var editedHeadings = CountClauseHeadings(editedText);
        if (originalHeadings > 0 && editedHeadings < originalHeadings - 1)
        {
            return "The assistant's reply appears to have removed clauses from the contract, so " +
                   "it was not applied. Nothing was changed. Try asking for one smaller, specific change.";
        }

        return null;
    }

    /// <summary>Counts numbered clause heading lines ("1. Title") in a document.</summary>
    private static int CountClauseHeadings(string text)
    {
        var count = 0;
        foreach (var line in text.Split('\n'))
        {
            if (ClauseHeadingPattern.IsMatch(line.TrimEnd('\r')))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Stages a snapshot of the contract's current text (without saving: the caller's
    /// SaveChanges persists it together with its own changes). Snapshots are skipped when
    /// there is no text yet — an empty document has no history worth keeping.
    /// </summary>
    private static void StageVersionSnapshot(DbContext database, Contract contract, string reason)
    {
        if (string.IsNullOrWhiteSpace(contract.AssembledText))
        {
            return;
        }

        var currentMax = database.Set<ContractVersion>()
            .Where(version => version.ContractId == contract.Id)
            .OrderByDescending(version => version.VersionNumber)
            .Select(version => (int?)version.VersionNumber)
            .FirstOrDefault();

        database.Set<ContractVersion>().Add(new ContractVersion
        {
            ContractId = contract.Id,
            VersionNumber = (currentMax ?? 0) + 1,
            Text = contract.AssembledText,
            Reason = reason,
            CreatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Deletes the oldest snapshots beyond <see cref="MaxVersionsPerContract"/>.</summary>
    private static async Task PruneOldVersionsAsync(DbContext database, int contractId)
    {
        var excess = await database.Set<ContractVersion>()
            .Where(version => version.ContractId == contractId)
            .OrderByDescending(version => version.VersionNumber)
            .Skip(MaxVersionsPerContract)
            .ToListAsync();

        if (excess.Count == 0)
        {
            return;
        }

        database.Set<ContractVersion>().RemoveRange(excess);
        await database.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes the oldest chat messages beyond <see cref="MaxChatMessagesPerContract"/>,
    /// keeping the newest ones the user actually reads (audit Finding #7).
    /// </summary>
    private static async Task PruneOldChatMessagesAsync(DbContext database, int contractId)
    {
        var excess = await database.Set<ChatMessage>()
            .Where(message => message.ContractId == contractId)
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Skip(MaxChatMessagesPerContract)
            .ToListAsync();

        if (excess.Count == 0)
        {
            return;
        }

        database.Set<ChatMessage>().RemoveRange(excess);
        await database.SaveChangesAsync();
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
