using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
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
        IChatService chatService,
        IDocxBuilder docxBuilder,
        IPdfBuilder pdfBuilder,
        IDocumentExportService exportService,
        INavigationService navigation,
        IWizardDialogService dialogService,
        IAppSettingsService settingsService)
    {
        _contractService = contractService ?? throw new ArgumentNullException(nameof(contractService));
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
            DocumentText = contract.AssembledText ?? string.Empty;
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

    /// <summary>Regenerates the document from the stored clause selection and intake fields (template-based; no AI).</summary>
    [RelayCommand]
    private async Task RegenerateAsync()
    {
        if (IsBusy || Contract is null) return;

        IsBusy = true;
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            // Empty clause ids keep the stored selection; AI polish is disabled because
            // there is currently no UI path that uses it (the caller must opt in explicitly).
            var text = await _contractService.GenerateContractAsync(Contract.Id, new List<int>(), useAiPolish: false);
            DocumentText = text;

            var refreshed = await _contractService.GetByIdAsync(Contract.Id);
            if (refreshed is not null)
            {
                Contract = refreshed;
            }

            await ReloadVersionsAsync(Contract.Id);
            InfoMessage = HasUnresolvedPlaceholders(text)
                ? "The contract was regenerated, but some placeholders could not be filled and " +
                  "are still visible as {{...}} in the document. Use the assistant to complete them."
                : "The contract was regenerated from its clauses and intake answers.";
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
