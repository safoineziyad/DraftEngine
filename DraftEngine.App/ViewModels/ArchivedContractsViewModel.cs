using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services;
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

    public string PageTitle => "Archived Contracts";

    public ObservableCollection<Contract> Contracts { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    public bool HasContracts => Contracts.Count > 0;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ArchivedContractsViewModel(
        IContractEditService contractEditService,
        IWizardDialogService dialogs)
    {
        _contractEditService = contractEditService ?? throw new ArgumentNullException(nameof(contractEditService));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
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
            ErrorMessage = "We couldn't load archived contracts. Please try again.";
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
            ErrorMessage = "We couldn't restore this contract. Please try again.";
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
                $"Permanently delete \"{contract.Title}\" and its history? This cannot be undone.",
                "Permanently delete contract"))
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
            ErrorMessage = "We couldn't permanently delete this contract. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
