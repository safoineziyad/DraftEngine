using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services;
using DraftEngine.App.ViewModels.NewContract;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// View model for the 4-step New Contract wizard: contract type, intake form,
/// clause selection and review/generate.
/// </summary>
public partial class NewContractViewModel : ObservableObject
{
    /// <summary>The only contract type currently implemented.</summary>
    internal const string MutualNdaType = "Mutual NDA";

    // The local user id used until authentication is available (single-user desktop app).
    private const int CurrentUserId = 1;

    // Default governing law offered on step 2. (Hook up to the Settings page when it
    // exposes a user preference; "California" is the built-in fallback.)
    private static readonly string[] GoverningLawChoices = { "California", "New York", "Delaware", "Texas" };

    private readonly IContractService _contractService;
    private readonly IClauseService _clauseService;
    private readonly IFieldService _fieldService;
    private readonly INavigationService _navigation;
    private readonly IWizardDialogService _dialogs;

    private bool _clausesLoaded;

    /// <summary>The page title shown in the view header.</summary>
    public string PageTitle => "New Contract";

    /// <summary>The current wizard step (1-4).</summary>
    [ObservableProperty] private int _currentStep = 1;

    /// <summary>Whether a contract is currently being generated.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>Whether the clause library is being loaded for step 3.</summary>
    [ObservableProperty] private bool _isLoadingClauses;

    /// <summary>The contract type selected on step 1.</summary>
    [ObservableProperty] private ContractTypeOptionViewModel? _selectedContractType;

    /// <summary>How many clauses are currently checked on step 3.</summary>
    [ObservableProperty] private int _selectedClauseCount;

    /// <summary>How many clauses were offered on step 3.</summary>
    [ObservableProperty] private int _totalClauseCount;

    /// <summary>The contract type cards shown on step 1.</summary>
    public ObservableCollection<ContractTypeOptionViewModel> ContractTypes { get; } = new();

    /// <summary>The dynamic intake fields shown on step 2.</summary>
    public ObservableCollection<IntakeFieldViewModel> IntakeFields { get; } = new();

    /// <summary>The clause checklist grouped by category, shown on step 3.</summary>
    public ObservableCollection<ClauseCategoryGroupViewModel> ClauseGroups { get; } = new();

    /// <summary>The step indicator entries shown above the wizard content.</summary>
    public ObservableCollection<WizardStepViewModel> Steps { get; } = new();

    /// <summary>The label/value rows of the step 4 review summary.</summary>
    public ObservableCollection<SummaryItemViewModel> SummaryItems { get; } = new();

    /// <summary>Shows the step 1 panel.</summary>
    public bool IsStep1Visible => CurrentStep == 1;

    /// <summary>Shows the step 2 panel.</summary>
    public bool IsStep2Visible => CurrentStep == 2;

    /// <summary>Shows the step 3 panel.</summary>
    public bool IsStep3Visible => CurrentStep == 3;

    /// <summary>Shows the step 4 panel.</summary>
    public bool IsStep4Visible => CurrentStep == 4;

    /// <summary>Shows the Back button (hidden on step 1).</summary>
    public bool IsBackVisible => CurrentStep > 1;

    /// <summary>Shows the Next button (hidden on the last step).</summary>
    public bool IsNextVisible => CurrentStep < 4;

    /// <summary>Shows the Generate button (step 4 only).</summary>
    public bool IsGenerateVisible => CurrentStep == 4;

    /// <summary>The text of the generate button ("Generating…" while busy).</summary>
    public string GenerateButtonText => IsBusy ? "Generating\u2026" : "Generate Contract";

    /// <summary>Creates the wizard view model and prepares a fresh wizard state.</summary>
    public NewContractViewModel(
        IContractService contractService,
        IClauseService clauseService,
        IFieldService fieldService,
        INavigationService navigation,
        IWizardDialogService dialogs)
    {
        _contractService = contractService;
        _clauseService = clauseService;
        _fieldService = fieldService;
        _navigation = navigation;
        _dialogs = dialogs;

        Steps.Add(new WizardStepViewModel(1, "Type"));
        Steps.Add(new WizardStepViewModel(2, "Details"));
        Steps.Add(new WizardStepViewModel(3, "Clauses"));
        Steps.Add(new WizardStepViewModel(4, "Review"));

        BuildContractTypes();
        BuildIntakeFields();
        UpdateStepVisibility();
        UpdateStepIndicators();

        // The wizard is a singleton page: start fresh every time it is opened.
        _navigation.CurrentViewModelChanged += OnPageChanged;
    }

    /// <summary>Selects an available contract type card on step 1.</summary>
    [RelayCommand]
    private void SelectContractType(ContractTypeOptionViewModel? option)
    {
        if (option is null || !option.IsAvailable)
        {
            return;
        }

        foreach (var type in ContractTypes)
        {
            type.IsSelected = ReferenceEquals(type, option);
        }

        SelectedContractType = option;
    }

    /// <summary>Advances the wizard one step (loads clauses when entering step 3, builds the summary for step 4).</summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (CurrentStep == 2)
        {
            if (_clausesLoaded)
            {
                RefreshClausePreviews();
            }
            else
            {
                await LoadClausesAsync();
            }
        }
        else if (CurrentStep == 3)
        {
            BuildSummary();
        }

        CurrentStep++;
    }

    /// <summary>Goes back one step.</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentStep--;

    private bool CanGoNext() => !IsBusy && CurrentStep switch
    {
        1 => SelectedContractType is not null,
        2 => IntakeFields.Count > 0 && IntakeFields.All(field => field.IsValid),
        3 => SelectedClauseCount > 0,
        _ => false
    };

    private bool CanGoBack() => !IsBusy && CurrentStep > 1;

    private void RefreshCommands()
    {
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        GenerateCommand.NotifyCanExecuteChanged();
    }

    private void OnPageChanged(object? viewModel)
    {
        if (ReferenceEquals(viewModel, this))
        {
            ResetWizard();
        }
    }

    private void ResetWizard()
    {
        IsBusy = false;
        IsLoadingClauses = false;
        CurrentStep = 1;
        SelectedContractType = null;
        foreach (var type in ContractTypes)
        {
            type.IsSelected = false;
        }

        BuildIntakeFields();
        ClauseGroups.Clear();
        SummaryItems.Clear();
        _clausesLoaded = false;
        SelectedClauseCount = 0;
        TotalClauseCount = 0;
    }

    private void BuildContractTypes()
    {
        ContractTypes.Clear();
        ContractTypes.Add(new ContractTypeOptionViewModel(
            "Mutual NDA",
            "Two-way confidentiality agreement between parties sharing sensitive information.",
            isAvailable: true));
        ContractTypes.Add(new ContractTypeOptionViewModel(
            "Non-Disclosure Agreement (One-Way)",
            "Protects information disclosed by a single party.",
            isAvailable: false));
        ContractTypes.Add(new ContractTypeOptionViewModel(
            "Independent Contractor Agreement",
            "Defines scope, fees and ownership for contract work.",
            isAvailable: false));
        ContractTypes.Add(new ContractTypeOptionViewModel(
            "Consulting Agreement",
            "Sets the terms of an ongoing advisory engagement.",
            isAvailable: false));
    }

    private void BuildIntakeFields()
    {
        IntakeFields.Clear();
        var fields = new[]
        {
            NewField("Party1", "Party 1 name"),
            NewField("Party1Address", "Party 1 address"),
            NewField("Party2", "Party 2 name"),
            NewField("Party2Address", "Party 2 address"),
            NewField("ClientName", "Client name"),
            NewField("ClientEmail", "Client email"),
            NewField("EffectiveDate", "Effective date", IntakeFieldKind.Date, defaultDate: DateTime.Today),
            NewField("GoverningLaw", "Governing law", IntakeFieldKind.Choice,
                choices: GoverningLawChoices, defaultValue: GoverningLawChoices[0]),
            NewField("TermMonths", "Term (months)", IntakeFieldKind.Number, defaultValue: "12"),
            NewField("Purpose", "Purpose of disclosure"),
        };

        foreach (var field in fields)
        {
            field.PropertyChanged += OnIntakeFieldChanged;
            IntakeFields.Add(field);
        }
    }

    private static IntakeFieldViewModel NewField(
        string key,
        string label,
        IntakeFieldKind kind = IntakeFieldKind.Text,
        string[]? choices = null,
        string? defaultValue = null,
        DateTime? defaultDate = null) =>
        new(key, label, kind, isRequired: true, choices, defaultValue, defaultDate);

    private void OnIntakeFieldChanged(object? sender, PropertyChangedEventArgs e) => RefreshCommands();

    /// <summary>The intake answers keyed by stable field key (used for ContractField rows and placeholder substitution).</summary>
    internal Dictionary<string, string> GetFieldValues() =>
        IntakeFields.ToDictionary(field => field.Key, field => field.EffectiveValue, StringComparer.OrdinalIgnoreCase);

    internal string FieldValue(string key) =>
        IntakeFields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))?
            .EffectiveValue ?? string.Empty;

    private void UpdateStepVisibility()
    {
        OnPropertyChanged(nameof(IsStep1Visible));
        OnPropertyChanged(nameof(IsStep2Visible));
        OnPropertyChanged(nameof(IsStep3Visible));
        OnPropertyChanged(nameof(IsStep4Visible));
        OnPropertyChanged(nameof(IsBackVisible));
        OnPropertyChanged(nameof(IsNextVisible));
        OnPropertyChanged(nameof(IsGenerateVisible));
    }

    private void UpdateStepIndicators()
    {
        foreach (var step in Steps)
        {
            step.IsCurrent = step.Index == CurrentStep;
            step.IsCompleted = step.Index < CurrentStep;
        }
    }

    partial void OnCurrentStepChanged(int value)
    {
        UpdateStepVisibility();
        UpdateStepIndicators();
        RefreshCommands();
    }

    partial void OnSelectedContractTypeChanged(ContractTypeOptionViewModel? value) => RefreshCommands();

    partial void OnSelectedClauseCountChanged(int value) => RefreshCommands();

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommands();
        OnPropertyChanged(nameof(GenerateButtonText));
    }
}
