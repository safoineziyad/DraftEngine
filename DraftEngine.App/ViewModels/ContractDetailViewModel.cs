using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using Microsoft.Win32;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// View model for the contract detail page: shows the generated document, offers
/// template-based regeneration, AI chat editing, and DOCX/PDF export. View models are
/// singletons, so everything reloads from the database on every navigation.
/// </summary>
public partial class ContractDetailViewModel : ObservableObject
{
    private readonly IContractService _contractService;
    private readonly IChatService _chatService;
    private readonly IDocxBuilder _docxBuilder;
    private readonly IPdfBuilder _pdfBuilder;
    private readonly IDocumentExportService _exportService;
    private readonly INavigationService _navigation;

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

    /// <summary>True when AI chat editing can be attempted (contract generated first).</summary>
    public bool CanChat => HasDocument && !IsBusy;

    /// <summary>Creates the view model.</summary>
    public ContractDetailViewModel(
        IContractService contractService,
        IChatService chatService,
        IDocxBuilder docxBuilder,
        IPdfBuilder pdfBuilder,
        IDocumentExportService exportService,
        INavigationService navigation)
    {
        _contractService = contractService ?? throw new ArgumentNullException(nameof(contractService));
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _docxBuilder = docxBuilder ?? throw new ArgumentNullException(nameof(docxBuilder));
        _pdfBuilder = pdfBuilder ?? throw new ArgumentNullException(nameof(pdfBuilder));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }

    /// <inheritdoc cref="INavigationAware" />
    public async void OnNavigatedTo(object? parameter)
    {
        var contractId = parameter as int?;
        if (contractId is null or <= 0)
        {
            MarkAsUnloaded("This contract could not be opened. Select it again from the Contracts list.");
            return;
        }

        await LoadAsync(contractId.Value);
    }

    /// <summary>Reloads the contract and its chat history.</summary>
    [RelayCommand]
    private async Task LoadAsync(int contractId)
    {
        if (IsBusy) return;

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
            // Empty clause ids keep the stored selection; AI polish is off so generation
            // also works without an API key (the wizard's AI polish applies when configured).
            var text = await _contractService.GenerateContractAsync(Contract.Id, new List<int>(), useAiPolish: false);
            DocumentText = text;

            var refreshed = await _contractService.GetByIdAsync(Contract.Id);
            if (refreshed is not null)
            {
                Contract = refreshed;
            }

            InfoMessage = "The contract was regenerated from its clauses and intake answers.";
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

    /// <summary>Exports the document as DOCX via a save-file dialog.</summary>
    [RelayCommand]
    private async Task ExportDocxAsync() => await ExportAsync(FileType.Docx);

    /// <summary>Exports the document as PDF via a save-file dialog.</summary>
    [RelayCommand]
    private async Task ExportPdfAsync() => await ExportAsync(FileType.Pdf);

    /// <summary>Navigates back to the contracts list.</summary>
    [RelayCommand]
    private void BackToList() => _navigation.Navigate("contracts");

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

        IsBusy = true;
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            var finalPath = fileType == FileType.Pdf
                ? await _pdfBuilder.BuildPdfAsync(Contract!, DocumentText, dialog.FileName)
                : await _docxBuilder.BuildDocxAsync(Contract!, DocumentText, dialog.FileName);

            await _exportService.RecordExportAsync(Contract!.Id, finalPath, fileType);

            var refreshed = await _contractService.GetByIdAsync(Contract.Id);
            if (refreshed is not null)
            {
                Contract = refreshed;
            }

            InfoMessage = $"Exported to {finalPath}";
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
        }
        catch (Exception ex)
        {
            // The chat history is supporting context: never block the page on it.
            Log.Warning(ex, "Failed to load the chat history for contract {ContractId}.", contractId);
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
    }

    private static string BuildDefaultFileName(Contract contract, string extension)
    {
        var raw = $"{contract.Title}-{contract.ContractType}";
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(raw.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return $"{safe.Trim('-', ' ', '.')}.{extension}";
    }

    partial void OnDocumentTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(CanChat));
    }

    partial void OnChatInputChanged(string value) => RaiseChatAvailability();

    partial void OnIsBusyChanged(bool value) => RaiseChatAvailability();

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnInfoMessageChanged(string? value) => OnPropertyChanged(nameof(HasInfo));

    private void RaiseChatAvailability()
    {
        SendChatEditCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanChat));
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
