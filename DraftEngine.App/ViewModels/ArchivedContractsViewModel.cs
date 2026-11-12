using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Localization;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>Lists archived contracts and provides restore and permanent-delete actions.</summary>
public partial class ArchivedContractsViewModel : ObservableObject, INavigationAware
{
    private const int CurrentUserId = 1;

    private readonly IContractEditService _contractEditService;
    private readonly IWizardDialogService _dialogs;
    private readonly ILocalizationService _localization;
    private string? _errorKey;
    private object[] _errorArgs = Array.Empty<object>();

    public string PageTitle => _localization.GetString("Nav.Archived");

    public ObservableCollection<Contract> Contracts { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    public bool HasContracts => Contracts.Count > 0;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ArchivedContractsViewModel(
        IContractEditService contractEditService,
        IWizardDialogService dialogs,
        ILocalizationService? localization = null)
    {
        _contractEditService = contractEditService ?? throw new ArgumentNullException(nameof(contractEditService));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _localization = localization ?? LocalizationSource.Current.Service
            ?? throw new ArgumentNullException(nameof(localization));
        _localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PageTitle));
        if (_errorKey is not null)
        {
            ErrorMessage = Text(_errorKey, _errorArgs);
        }
    }

    private string Text(string key, params object[] args) => _localization.GetString(key, args);

    private void SetError(string key, params object[] args)
    {
        _errorKey = key;
        _errorArgs = args;
        ErrorMessage = Text(key, args);
    }

    public async void OnNavigatedTo(object? parameter) => await LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var archived = await _contractEditService.GetArchivedContractsAsync(CurrentUserId);
            Contracts.Clear();
            foreach (var contract in archived)
            {
                Contracts.Add(contract);
            }

            OnPropertyChanged(nameof(HasContracts));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load archived contracts.");
            SetError("Archived.ErrorLoad");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync(Contract? contract)
    {
        if (contract is null || IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            if (await _contractEditService.RestoreContractAsync(contract.Id))
            {
                Contracts.Remove(contract);
                OnPropertyChanged(nameof(HasContracts));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore archived contract {ContractId}.", contract.Id);
            SetError("Archived.ErrorRestore");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PermanentlyDeleteAsync(Contract? contract)
    {
        if (contract is null || IsLoading)
        {
            return;
        }

        if (!_dialogs.Confirm(
                Text("Archived.DeleteConfirm", contract.Title),
                Text("Archived.DeleteConfirmTitle")))
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await _contractEditService.PermanentDeleteContractAsync(contract.Id);
            Contracts.Remove(contract);
            OnPropertyChanged(nameof(HasContracts));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to permanently delete archived contract {ContractId}.", contract.Id);
            SetError("Archived.ErrorDelete");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
