using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.ViewModels.NewContract;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Models.ContractTypes;
using Microsoft.Win32;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// View model for the contract detail page: shows the generated document, offers
/// template-based regeneration, AI chat editing, and DOCX/PDF export. View models are
/// singletons, so everything reloads from the database on every navigation.
/// </summary>
public partial class ContractDetailViewModel : ObservableObject, INavigationAware
{
    private readonly IContractService _contractService;
    private readonly IContractEditService _contractEditService;
    private readonly IFieldService _fieldService;
    private readonly IClauseService _clauseService;
    private readonly IContractTypeRegistry _contractTypeRegistry;
    private readonly IChatService _chatService;
    private readonly IDocxBuilder _docxBuilder;
    private readonly IPdfBuilder _pdfBuilder;
    private readonly IDocumentExportService _exportService;
    private readonly INavigationService _navigation;
    private readonly IWizardDialogService _dialogService;
    private readonly IAppSettingsService _settingsService;

    /// <summary>
    /// Contract id requested while an operation was still running; loaded as soon as the
    /// busy state clears, so a navigation request is never silently dropped.
    /// </summary>
    private int? _pendingContractId;

    /// <summary>The page title shown in the view header (the contract title once loaded).</summary>
    [ObservableProperty]
    private string pageTitle = "Contract Detail";

    /// <summary>The editable title staged until the user saves and regenerates.</summary>
    [ObservableProperty]
    private string editTitle = string.Empty;

    /// <summary>Registry-defined intake values for this contract type.</summary>
    public ObservableCollection<IntakeFieldViewModel> IntakeFields { get; } = new();

    /// <summary>Available and currently selected clauses for this contract type.</summary>
    public ObservableCollection<ClauseCategoryGroupViewModel> ClauseGroups { get; } = new();

    /// <summary>Number of clauses selected for the next regeneration.</summary>
    [ObservableProperty]
    private int selectedClauseCount;

    /// <summary>Whether template output should be polished with AI during regeneration.</summary>
    [ObservableProperty]
    private bool useAiPolish;

    /// <summary>Whether the current metadata, intake and clause selection can be saved.</summary>
    public bool CanSaveAndRegenerate =>
        Contract is not null &&
        !IsBusy &&
        SelectedClauseCount > 0 &&
        IntakeFields.All(field => field.IsValid) &&
        !string.IsNullOrWhiteSpace(EditTitle);

    /// <summary>Whether staged contract settings can currently be changed.</summary>
    public bool CanEditContract => Contract is not null && !IsBusy;

    /// <summary>The loaded contract, or null when it could not be loaded.</summary>
    [ObservableProperty]
    private Contract? contract;

    /// <summary>The current document text shown in the editor panel.</summary>
    [ObservableProperty]
    private string documentText = string.Empty;

    /// <summary>Chat history rows displayed in the assistant panel.</summary>
    public ObservableCollection<ChatMessageRowViewModel> ChatMessages { get; } = new();

    /// <summary>Version history rows (newest first) shown in the document panel.</summary>
    public ObservableCollection<ContractVersionRowViewModel> Versions { get; } = new();

    /// <summary>True when the version history panel is expanded.</summary>
    [ObservableProperty]
    private bool isHistoryVisible;

    /// <summary>Header text of the version history panel, including the snapshot count.</summary>
    public string VersionHistoryHeader =>
        Versions.Count == 0 ? "Version history" : $"Version history ({Versions.Count})";

    /// <summary>True when no snapshots exist yet (hint text shown instead of the list).</summary>
    public bool IsVersionHistoryEmpty => Versions.Count == 0;

    /// <summary>The draft of the next chat instruction.</summary>
    [ObservableProperty]
    private string chatInput = string.Empty;

    /// <summary>True while a contract operation (load, edit, regenerate, export) is running.</summary>
    [ObservableProperty]
    private bool isBusy;

    /// <summary>Friendly error message shown in the error banner; null when there is no error.</summary>
    [ObservableProperty]
    private string? errorMessage;

    /// <summary>Friendly success message shown in the info banner; null when there is none.</summary>
    [ObservableProperty]
    private string? infoMessage;

    /// <summary>True when the contract could not be loaded (missing or invalid id).</summary>
    [ObservableProperty]
    private bool loadFailed;

    /// <summary>Message shown in the load-failed state.</summary>
    [ObservableProperty]
    private string loadFailedMessage = string.Empty;

    /// <summary>True when a friendly error is displayed.</summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>True when a friendly success message is displayed.</summary>
    public bool HasInfo => InfoMessage is not null;

    /// <summary>True when the document panel should be visible.</summary>
    public bool HasDocument => !string.IsNullOrWhiteSpace(DocumentText);

    /// <summary>True when the chat panel has no messages yet (hint shown instead).</summary>
    public bool IsChatEmpty => ChatMessages.Count == 0;

    /// <summary>True when an OpenAI API key is configured in settings.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_settingsService.Current.OpenAiApiKey);

    /// <summary>True when a warning about missing API key should be shown.</summary>
    public bool ShowApiKeyWarning => HasDocument && !HasApiKey && !LoadFailed;

    /// <summary>True when AI chat editing can be attempted (contract generated first and API key configured).</summary>
    public bool CanChat => HasDocument && !IsBusy && HasApiKey;

    /// <summary>Creates the view model.</summary>
    public ContractDetailViewModel(
        IContractService contractService,
        IContractEditService contractEditService,
        IFieldService fieldService,
        IClauseService clauseService,
        IContractTypeRegistry contractTypeRegistry,
        IChatService chatService,
        IDocxBuilder docxBuilder,
        IPdfBuilder pdfBuilder,
        IDocumentExportService exportService,
        INavigationService navigation,
        IWizardDialogService dialogService,
        IAppSettingsService settingsService)
    {
        _contractService = contractService ?? throw new ArgumentNullException(nameof(contractService));
        _contractEditService = contractEditService ?? throw new ArgumentNullException(nameof(contractEditService));
        _fieldService = fieldService ?? throw new ArgumentNullException(nameof(fieldService));
        _clauseService = clauseService ?? throw new ArgumentNullException(nameof(clauseService));
        _contractTypeRegistry = contractTypeRegistry ?? throw new ArgumentNullException(nameof(contractTypeRegistry));
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _docxBuilder = docxBuilder ?? throw new ArgumentNullException(nameof(docxBuilder));
        _pdfBuilder = pdfBuilder ?? throw new ArgumentNullException(nameof(pdfBuilder));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    /// <inheritdoc cref="INavigationAware" />
    public async void OnNavigatedTo(object? parameter)
    {
        // The INavigationAware contract is async void (WPF navigation calls it synchronously),
        // so the whole body is guarded: no exception may ever escape into the dispatcher and
        // take the application down (audit Finding #5).
        try
        {
            RaiseChatAvailability();
            var contractId = parameter as int?;
            if (contractId is null or <= 0)
            {
                MarkAsUnloaded("This contract could not be opened. Select it again from the Contracts list.");
                return;
            }

            await LoadAsync(contractId.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected failure while opening the contract detail page.");
            MarkAsUnloaded("We couldn't open this contract. Please try again.");
        }
    }

    /// <summary>Reloads the contract and its chat history.</summary>
    [RelayCommand]
    private async Task LoadAsync(int contractId)
    {
        if (IsBusy)
        {
            // Another operation is still running (the user opened a different contract
            // while an AI chat edit was in flight). The request is remembered instead of
            // dropped: dropping it left the page showing the previous contract, and the
            // next edit or revert was then applied to the wrong contract. The pending
            // load is picked up as soon as the busy state clears.
            _pendingContractId = contractId;
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        InfoMessage = null;
        Contract = null;
        ResetEditor();
        try
        {
            var contract = await _contractService.GetByIdAsync(contractId);
            if (contract is null)
            {
                MarkAsUnloaded("This contract no longer exists. It may have been deleted.");
                return;
            }

            LoadFailed = false;
            Contract = contract;
            PageTitle = contract.Title;
            EditTitle = contract.Title;
            DocumentText = contract.AssembledText ?? string.Empty;
            await LoadEditorAsync(contract);
            await ReloadChatAsync(contractId);
            await ReloadVersionsAsync(contractId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load contract {ContractId}.", contractId);
            MarkAsUnloaded("We couldn't load this contract. Please try again.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Persists edited metadata, intake answers and clause selection, then regenerates the document.</summary>
    [RelayCommand]
    private async Task RegenerateAsync()
    {
        if (!CanSaveAndRegenerate || Contract is null) return;

        IsBusy = true;
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            var contractId = Contract.Id;
            var title = EditTitle.Trim();
            var useAiPolish = UseAiPolish;
            var values = GetIntakeValues();
            var definition = _contractTypeRegistry.TryGetByDisplayName(Contract.ContractType);
            if (definition is null)
            {
                ErrorMessage = "This contract type is no longer available for editing.";
                return;
            }

            var selectedClauseIds = ClauseGroups
                .SelectMany(group => group.Clauses)
                .Where(clause => clause.IsSelected)
                .Select(clause => clause.Id)
                .Distinct()
                .ToList();
            if (selectedClauseIds.Count == 0)
            {
                ErrorMessage = "Select at least one clause before regenerating.";
                return;
            }

            var updated = await _contractEditService.UpdateContractAsync(contractId, current =>
            {
                current.Title = title;
                current.ClientName = GetMetadataValue(values, "ClientName", definition.SecondPartyNameKey, current.ClientName);
                if (values.TryGetValue("ClientEmail", out var clientEmail))
                {
                    current.ClientEmail = clientEmail.Trim();
                }

                current.EffectiveDate = GetEffectiveDate(values, definition, current.EffectiveDate);
                current.GoverningLaw = GetMetadataValue(values, "GoverningLaw", fallback: current.GoverningLaw);

                var termMonths = GetMetadataValue(values, "TermMonths", fallback: string.Empty);
                if (int.TryParse(termMonths, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTerm) &&
                    parsedTerm > 0)
                {
                    current.TermMonths = parsedTerm;
                }

                current.PartiesJson = BuildPartiesJson(definition, values);
            });
            if (updated is null)
            {
                MarkAsUnloaded("This contract no longer exists. It may have been deleted.");
                return;
            }

            await _fieldService.MapFieldsAsync(contractId, values);
            var text = await _contractService.GenerateContractAsync(
                contractId,
                selectedClauseIds,
                useAiPolish: useAiPolish);
            DocumentText = text;

            var refreshed = await _contractService.GetByIdAsync(contractId);
            if (refreshed is not null)
            {
                Contract = refreshed;
                PageTitle = refreshed.Title;
            }

            await ReloadVersionsAsync(contractId);
            InfoMessage = HasUnresolvedPlaceholders(text)
                ? "The contract was regenerated, but some placeholders could not be filled and " +
                  "are still visible as {{...}} in the document. Use the assistant to complete them."
                : "Your changes were saved and the contract was regenerated.";
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Regeneration rejected for contract {ContractId}.", Contract.Id);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to regenerate contract {ContractId}.", Contract.Id);
            ErrorMessage = "We couldn't regenerate the contract. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Creates a draft copy of the current contract and opens it.</summary>
    [RelayCommand]
    private async Task DuplicateContractAsync()
    {
        if (IsBusy || Contract is null) return;

        IsBusy = true;
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            var copy = await _contractEditService.DuplicateContractAsync(
                Contract.Id,
                $"Copy of {Contract.Title}");
            if (copy is null)
            {
                ErrorMessage = "We couldn't duplicate this contract because it no longer exists.";
                return;
            }

            _navigation.Navigate("contract-detail", copy.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to duplicate contract {ContractId}.", Contract.Id);
            ErrorMessage = "We couldn't duplicate the contract. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadEditorAsync(Contract contract)
    {
        ResetEditor();
        EditTitle = contract.Title;

        var definition = _contractTypeRegistry.TryGetByDisplayName(contract.ContractType);
        if (definition is null)
        {
            return;
        }

        var storedValues = contract.ContractFields
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var partyValues = ReadPartyValues(contract.PartiesJson);
        foreach (var intake in definition.IntakeFields)
        {
            var kind = intake.Kind switch
            {
                "Date" => IntakeFieldKind.Date,
                "Choice" => IntakeFieldKind.Choice,
                "Number" => IntakeFieldKind.Number,
                _ => IntakeFieldKind.Text
            };
            var initial = GetInitialFieldValue(intake.Key, storedValues, partyValues, contract, definition);
            var choices = intake.Choices ?? Array.Empty<string>();
            if (kind == IntakeFieldKind.Choice &&
                !string.IsNullOrWhiteSpace(initial) &&
                !choices.Contains(initial, StringComparer.OrdinalIgnoreCase))
            {
                choices = choices.Append(initial).ToArray();
            }

            var field = new IntakeFieldViewModel(
                intake.Key,
                intake.Label,
                kind,
                intake.Required,
                choices,
                kind == IntakeFieldKind.Choice ? initial : null,
                kind == IntakeFieldKind.Date && DateTime.TryParse(initial, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date)
                    ? date
                    : null);
            if (kind is IntakeFieldKind.Text or IntakeFieldKind.Number)
            {
                field.Value = initial;
            }

            field.PropertyChanged += OnEditorFieldChanged;
            IntakeFields.Add(field);
        }

        var clauses = await _clauseService.GetClausesByContractTypeAsync(definition.Id);
        var storedSelections = contract.ContractClauses
            .GroupBy(selection => selection.ClauseId)
            .ToDictionary(group => group.Key, group => group.Last().Selected);
        var availableIds = clauses.Select(clause => clause.Id).ToHashSet();
        foreach (var selection in contract.ContractClauses)
        {
            if (selection.Clause is not null && availableIds.Add(selection.ClauseId))
            {
                clauses.Add(selection.Clause);
            }
        }

        var values = GetIntakeValues();
        foreach (var group in clauses
            .GroupBy(clause => clause.Category)
            .OrderBy(group => group.Min(clause => clause.SortOrder)))
        {
            var groupViewModel = new ClauseCategoryGroupViewModel(group.Key);
            foreach (var clause in group.OrderBy(clause => clause.SortOrder))
            {
                var option = new ClauseOptionViewModel(
                    clause.Id,
                    clause.Title,
                    clause.BodyTemplate,
                    clause.IsOptional,
                    storedSelections.TryGetValue(clause.Id, out var selected) ? selected : clause.IsDefaultSelected,
                    _fieldService.ReplacePlaceholders(clause.BodyTemplate, values));
                option.PropertyChanged += OnClauseOptionChanged;
                groupViewModel.Clauses.Add(option);
            }

            ClauseGroups.Add(groupViewModel);
        }

        UpdateSelectedClauseCount();
    }

    private void ResetEditor()
    {
        foreach (var field in IntakeFields)
        {
            field.PropertyChanged -= OnEditorFieldChanged;
        }

        foreach (var clause in ClauseGroups.SelectMany(group => group.Clauses))
        {
            clause.PropertyChanged -= OnClauseOptionChanged;
        }

        IntakeFields.Clear();
        ClauseGroups.Clear();
        SelectedClauseCount = 0;
        EditTitle = string.Empty;
        UseAiPolish = false;
        OnPropertyChanged(nameof(CanSaveAndRegenerate));
    }

    private Dictionary<string, string> GetIntakeValues() =>
        IntakeFields.ToDictionary(field => field.Key, field => field.EffectiveValue, StringComparer.OrdinalIgnoreCase);

    private void OnEditorFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanSaveAndRegenerate));
        RegenerateCommand.NotifyCanExecuteChanged();
    }

    private void OnClauseOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClauseOptionViewModel.IsSelected))
        {
            UpdateSelectedClauseCount();
        }
    }

    private void UpdateSelectedClauseCount()
    {
        SelectedClauseCount = ClauseGroups.SelectMany(group => group.Clauses).Count(clause => clause.IsSelected);
        OnPropertyChanged(nameof(CanSaveAndRegenerate));
        RegenerateCommand.NotifyCanExecuteChanged();
    }

    private static string GetInitialFieldValue(
        string key,
        IReadOnlyDictionary<string, string> storedValues,
        IReadOnlyList<(string Name, string Address)> parties,
        Contract contract,
        ContractTypeDefinition definition)
    {
        if (storedValues.TryGetValue(key, out var storedValue))
        {
            return storedValue;
        }

        if (string.Equals(key, definition.FirstPartyNameKey, StringComparison.OrdinalIgnoreCase))
        {
            return parties.ElementAtOrDefault(0).Name ?? string.Empty;
        }

        if (string.Equals(key, definition.FirstPartyAddressKey, StringComparison.OrdinalIgnoreCase))
        {
            return parties.ElementAtOrDefault(0).Address ?? string.Empty;
        }

        if (string.Equals(key, definition.SecondPartyNameKey, StringComparison.OrdinalIgnoreCase))
        {
            return parties.ElementAtOrDefault(1).Name ?? contract.ClientName;
        }

        if (string.Equals(key, definition.SecondPartyAddressKey, StringComparison.OrdinalIgnoreCase))
        {
            return parties.ElementAtOrDefault(1).Address ?? string.Empty;
        }

        return key switch
        {
            "ClientName" => contract.ClientName,
            "ClientEmail" => contract.ClientEmail,
            "EffectiveDate" => contract.EffectiveDate?.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture) ?? string.Empty,
            "GoverningLaw" => contract.GoverningLaw,
            "TermMonths" => contract.TermMonths.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };
    }

    private static string GetMetadataValue(
        IReadOnlyDictionary<string, string> values,
        string key,
        string? fallbackKey = null,
        string fallback = "")
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallbackKey) &&
            values.TryGetValue(fallbackKey, out value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return fallback;
    }

    private static DateTime? GetEffectiveDate(
        IReadOnlyDictionary<string, string> values,
        ContractTypeDefinition definition,
        DateTime? fallback)
    {
        var dateField = definition.IntakeFields.FirstOrDefault(field =>
            string.Equals(field.Key, "EffectiveDate", StringComparison.OrdinalIgnoreCase));
        var value = dateField is null ? null : values.GetValueOrDefault(dateField.Key);
        if (dateField is null)
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;
    }

    private static string BuildPartiesJson(
        ContractTypeDefinition definition,
        IReadOnlyDictionary<string, string> values)
    {
        var parties = new List<Dictionary<string, string>>();
        AddParty(parties, values, definition.FirstPartyNameKey, definition.FirstPartyAddressKey, "Party1", "Party1Address");
        AddParty(parties, values, definition.SecondPartyNameKey, definition.SecondPartyAddressKey, "Party2", "Party2Address");
        return JsonSerializer.Serialize(parties);
    }

    private static void AddParty(
        ICollection<Dictionary<string, string>> parties,
        IReadOnlyDictionary<string, string> values,
        string nameKey,
        string addressKey,
        string fallbackNameKey,
        string fallbackAddressKey)
    {
        var name = GetMetadataValue(values, nameKey, fallbackNameKey);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        parties.Add(new Dictionary<string, string>
        {
            ["name"] = name,
            ["address"] = GetMetadataValue(values, addressKey, fallbackAddressKey)
        });
    }

    private static IReadOnlyList<(string Name, string Address)> ReadPartyValues(string? partiesJson)
    {
        if (string.IsNullOrWhiteSpace(partiesJson))
        {
            return Array.Empty<(string, string)>();
        }

        try
        {
            using var document = JsonDocument.Parse(partiesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<(string, string)>();
            }

            return document.RootElement.EnumerateArray()
                .Where(party => party.ValueKind == JsonValueKind.Object)
                .Select(party => (
                    Name: ReadJsonProperty(party, "name"),
                    Address: ReadJsonProperty(party, "address")))
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    private static string ReadJsonProperty(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : string.Empty;
            }
        }

        return string.Empty;
    }

    /// <summary>Sends the current instruction to the AI assistant and applies the edit to the document.</summary>
    [RelayCommand]
    private async Task SendChatEditAsync()
    {
        if (IsBusy || Contract is null) return;

        var instruction = ChatInput?.Trim();
        if (string.IsNullOrWhiteSpace(instruction)) return;

        if (!HasDocument)
        {
            ErrorMessage = "Generate the contract before asking for edits.";
            return;
        }

        // First AI edit only: explain that the contract text leaves this computer
        // (audit Finding #10) and record the acknowledgement so it is never asked again.
        if (!await EnsureAiDataConsentAsync())
        {
            InfoMessage = "The instruction was not sent. AI edits share the contract text with OpenAI.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            var updatedText = await _contractService.ApplyChatEditAsync(Contract.Id, instruction);
            DocumentText = updatedText;
            ChatInput = string.Empty;
            Contract!.AssembledText = updatedText;
            await ReloadChatAsync(Contract.Id);
            await ReloadVersionsAsync(Contract.Id);

            if (HasUnresolvedPlaceholders(updatedText))
            {
                InfoMessage = "Some placeholders are still visible as {{...}} in the document — " +
                              "ask the assistant to fill them in.";
            }
        }
        catch (InvalidOperationException ex)
        {
            // The service throws user-friendly messages (missing API key, quota, network…).
            Log.Warning(ex, "Chat edit rejected for contract {ContractId}.", Contract.Id);
            ErrorMessage = ex.Message;
            await ReloadChatAsync(Contract.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Chat edit failed for contract {ContractId}.", Contract.Id);
            ErrorMessage = "The assistant could not apply the edit. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Restores the document text from an earlier version-history snapshot. The text being
    /// replaced is snapshotted first, so the revert itself can be undone from the same list.
    /// </summary>
    [RelayCommand]
    private async Task RevertToVersionAsync(int versionId)
    {
        if (IsBusy || Contract is null) return;

        IsBusy = true;
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            var restoredText = await _contractService.RevertToVersionAsync(Contract.Id, versionId);
            DocumentText = restoredText;
            Contract!.AssembledText = restoredText;

            var refreshed = await _contractService.GetByIdAsync(Contract.Id);
            if (refreshed is not null)
            {
                Contract = refreshed;
            }

            await ReloadVersionsAsync(Contract.Id);
            InfoMessage = "The earlier version was restored. The text it replaced was kept in the version history.";
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Version revert rejected for contract {ContractId}.", Contract.Id);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to revert contract {ContractId} to version {VersionId}.", Contract.Id, versionId);
            ErrorMessage = "We couldn't restore that version. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Exports the document as DOCX via a save-file dialog.</summary>
    [RelayCommand]
    private async Task ExportDocxAsync() => await ExportAsync(FileType.Docx);
    /// <summary>Exports the document as PDF via a save-file dialog.</summary>
    [RelayCommand]
    private async Task ExportPdfAsync() => await ExportAsync(FileType.Pdf);

    /// <summary>Navigates back to the contracts list.</summary>
    [RelayCommand]
    private void BackToList() => _navigation.Navigate("contracts");

    /// <summary>Navigates to the Settings page.</summary>
    [RelayCommand]
    private void GoToSettings() => _navigation.Navigate("settings");

    private async Task ExportAsync(FileType fileType)
    {
        if (IsBusy || Contract is null) return;

        if (!HasDocument)
        {
            ErrorMessage = "Generate the contract before exporting it.";
            return;
        }

        var extension = fileType == FileType.Pdf ? "pdf" : "docx";
        var dialog = new SaveFileDialog
        {
            Title = $"Export contract as {extension.ToUpperInvariant()}",
            FileName = BuildDefaultFileName(Contract!, extension),
            DefaultExt = $".{extension}",
            Filter = $"{extension.ToUpperInvariant()} document (*.{extension})|*.{extension}",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        // The dialog usually yields a safe path, but a hand-typed name can still carry
        // invalid characters or a Windows-reserved device name (audit Finding #12).
        var targetPath = SanitizeExportPath(dialog.FileName, extension);

        IsBusy = true;
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            var finalPath = fileType == FileType.Pdf
                ? await _pdfBuilder.BuildPdfAsync(Contract!, DocumentText, targetPath)
                : await _docxBuilder.BuildDocxAsync(Contract!, DocumentText, targetPath);

            await _exportService.RecordExportAsync(Contract!.Id, finalPath, fileType);

            var refreshed = await _contractService.GetByIdAsync(Contract.Id);
            if (refreshed is not null)
            {
                Contract = refreshed;
            }

            InfoMessage = $"Exported to {finalPath}";

            // Offer to open the exported file right away (audit Finding #20); declining or
            // a failing shell open must never turn a successful export into an error.
            if (_dialogService.Confirm(
                    "The document was exported successfully. Open it now?", "Export complete",
                    MessageBoxImage.Information))
            {
                OpenExportedFile(finalPath);
            }
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Export rejected for contract {ContractId}.", Contract.Id);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export contract {ContractId} as {FileType}.", Contract.Id, fileType);
            ErrorMessage = $"We couldn't export the contract as {extension.ToUpperInvariant()}. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadChatAsync(int contractId)
    {
        try
        {
            var messages = await _chatService.GetMessagesAsync(contractId) ?? new List<ChatMessage>();

            ChatMessages.Clear();
            foreach (var message in messages)
            {
                ChatMessages.Add(new ChatMessageRowViewModel(message));
            }

            // IsChatEmpty is a computed property: without this notification the "Example:"
            // hint stayed pinned above the history for the rest of the session.
            OnPropertyChanged(nameof(IsChatEmpty));
        }
        catch (Exception ex)
        {
            // The chat history is supporting context: never block the page on it.
            Log.Warning(ex, "Failed to load the chat history for contract {ContractId}.", contractId);
        }
    }

    /// <summary>Reloads the version history (newest first); failures never block the page.</summary>
    private async Task ReloadVersionsAsync(int contractId)
    {
        try
        {
            var versions = await _contractService.GetVersionsAsync(contractId);

            Versions.Clear();
            foreach (var version in versions.Reverse())
            {
                Versions.Add(new ContractVersionRowViewModel(version));
            }

            OnPropertyChanged(nameof(VersionHistoryHeader));
            OnPropertyChanged(nameof(IsVersionHistoryEmpty));
        }
        catch (Exception ex)
        {
            // Supporting context only: the page stays usable without the history list.
            Log.Warning(ex, "Failed to load the version history for contract {ContractId}.", contractId);
        }
    }

    private void MarkAsUnloaded(string message)
    {
        LoadFailed = true;
        LoadFailedMessage = message;
        Contract = null;
        ResetEditor();
        DocumentText = string.Empty;
        PageTitle = "Contract Detail";
        ChatMessages.Clear();
        OnPropertyChanged(nameof(IsChatEmpty));
    }

    private static string BuildDefaultFileName(Contract contract, string extension)
    {
        var raw = $"{contract.Title}-{contract.ContractType}";
        return SanitizeFileName(raw, extension);
    }

    /// <summary>
    /// Rebuilds an exported file path so it is always writable: invalid filename characters
    /// are replaced, surrounding spaces and dots are trimmed, empty names fall back to a
    /// generic one, and Windows-reserved device names (CON, NUL, COM1…) are prefixed.
    /// </summary>
    private static string SanitizeExportPath(string path, string extension)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        return Path.Combine(directory, SanitizeFileName(name, extension));
    }

    /// <summary>Sanitizes a bare file name (no directory) and appends the extension.</summary>
    private static string SanitizeFileName(string rawName, string extension)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(rawName.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
            .Trim(' ', '.');

        if (safe.Length == 0)
        {
            safe = "Contract";
        }

        if (ReservedFileNames.Contains(safe))
        {
            safe = "_" + safe;
        }

        return $"{safe}.{extension}";
    }

    /// <summary>Windows device names that cannot be used as file names.</summary>
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Opens a file with the Windows shell; failures are logged, never surfaced as errors.</summary>
    private static void OpenExportedFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // The export itself succeeded; an unopenable file (deleted, no handler) is not an error.
            Log.Warning(ex, "Failed to open the exported file {Path}.", path);
        }
    }

    /// <summary>True when the text still contains raw <c>{{...}}</c> placeholders.</summary>
    private static bool HasUnresolvedPlaceholders(string text) =>
        text.Contains("{{", StringComparison.Ordinal);

    /// <summary>
    /// Asks for the AI data-sharing acknowledgement before the first chat edit, and persists
    /// it so the question is only ever shown once (audit Finding #10). Declining is free of
    /// consequences: nothing is sent and the chat box keeps the typed instruction.
    /// </summary>
    private async Task<bool> EnsureAiDataConsentAsync()
    {
        if (_settingsService.Current.AiDataConsentAccepted)
        {
            return true;
        }

        var accepted = _dialogService.Confirm(
            "AI edits share this contract's text with OpenAI for processing.\n\n" +
            "Only continue if you are comfortable sending this document to OpenAI — for " +
            "example, if it contains no privileged or confidential client material you are " +
            "not permitted to share with a third-party service.\n\n" +
            "Continue with AI editing?",
            "AI editing sends data to OpenAI",
            MessageBoxImage.Information);

        if (!accepted)
        {
            return false;
        }

        _settingsService.Current.AiDataConsentAccepted = true;
        await _settingsService.SaveAsync(_settingsService.Current);
        return true;
    }

    partial void OnDocumentTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(CanChat));
        OnPropertyChanged(nameof(ShowApiKeyWarning));
    }

    partial void OnContractChanged(Contract? value)
    {
        OnPropertyChanged(nameof(CanSaveAndRegenerate));
        OnPropertyChanged(nameof(CanEditContract));
    }

    partial void OnEditTitleChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveAndRegenerate));
        RegenerateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsHistoryVisibleChanged(bool value)
    {
        // The Toolkit-generated property-changed hook cannot be awaited; invoke safely
        // (async void with internal try/catch) so no unobserved exceptions escape.
        SafeReloadVersions(value);
    }

    /// <summary>Invokes version history reload without fire-and-forget leakage.</summary>
    private async void SafeReloadVersions(bool value)
    {
        try
        {
            await RefreshVersionsAsync(value);
        }
        catch (Exception ex)
        {
            // Defensive guard: RefreshVersionsAsync already handles/report failures.
            Log.Warning(ex, "Unexpected error while reloading version history.");
        }
    }

    /// <summary>Reloads the version snapshots when the history panel is opened, reporting failures to the user.</summary>
    private async Task RefreshVersionsAsync(bool visible)
    {
        try
        {
            if (visible && Contract is not null)
            {
                await ReloadVersionsAsync(Contract.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to reload versions");
            ErrorMessage = "Could not load version history.";
        }
    }

    partial void OnChatInputChanged(string value) => RaiseChatAvailability();

    partial void OnIsBusyChanged(bool value)
    {
        RaiseChatAvailability();
        OnPropertyChanged(nameof(ShowApiKeyWarning));
        OnPropertyChanged(nameof(CanSaveAndRegenerate));
        OnPropertyChanged(nameof(CanEditContract));
        RegenerateCommand.NotifyCanExecuteChanged();

        if (!value)
        {
            // The operation just finished: pick up a navigation request that arrived
            // while it was running.
            _ = LoadPendingContractAsync();
        }
    }

    /// <summary>
    /// Loads the contract whose navigation request arrived while an operation was running.
    /// </summary>
    private async Task LoadPendingContractAsync()
    {
        try
        {
            var pending = _pendingContractId;
            _pendingContractId = null;

            if (pending is null)
            {
                return;
            }

            if (Contract?.Id == pending.Value)
            {
                return; // The page already shows that contract.
            }

            await LoadAsync(pending.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected failure while loading the contract requested during a running operation.");
        }
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnInfoMessageChanged(string? value) => OnPropertyChanged(nameof(HasInfo));

    private void RaiseChatAvailability()
    {
        SendChatEditCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanChat));
        OnPropertyChanged(nameof(HasApiKey));
        OnPropertyChanged(nameof(ShowApiKeyWarning));
    }
}

/// <summary>
/// Display row for one chat message: bubbles the role, content and local time label.
/// </summary>
public partial class ChatMessageRowViewModel : ObservableObject
{
    /// <summary>Creates the row from the persisted message.</summary>
    public ChatMessageRowViewModel(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        IsUser = message.Role == MessageRole.User;
        Content = message.Content;
        TimeLabel = message.CreatedAt.ToLocalTime().ToString("HH:mm");
    }

    /// <summary>True for user messages (right-aligned bubble); false for assistant messages.</summary>
    public bool IsUser { get; }

    /// <summary>The message text.</summary>
    public string Content { get; }

    /// <summary>The local creation time, shown as HH:mm.</summary>
    public string TimeLabel { get; }
}

/// <summary>
/// Display row for one version-history snapshot: version number, why it was taken
/// and when, plus the identifier the revert command needs.
/// </summary>
public sealed class ContractVersionRowViewModel
{
    /// <summary>Creates the row from the persisted snapshot.</summary>
    public ContractVersionRowViewModel(ContractVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        VersionId = version.Id;
        VersionLabel = $"#{version.VersionNumber}";
        DetailsLabel = $"{version.Reason} · {version.CreatedAt.ToLocalTime():g}";
    }

    /// <summary>Database identifier passed to the revert command.</summary>
    public int VersionId { get; }

    /// <summary>Short version label, e.g. "#3".</summary>
    public string VersionLabel { get; }

    /// <summary>Human-readable reason and local timestamp of the snapshot.</summary>
    public string DetailsLabel { get; }
}
