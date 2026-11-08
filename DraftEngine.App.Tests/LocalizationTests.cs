using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.ViewModels;
using DraftEngine.App.ViewModels.NewContract;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Localization behaviour tests: live switching, event/notification contract, RTL,
/// European digits, persistence and missing-key fallback. These tests do not create
/// WPF windows, so they run on the normal xUnit thread; window rendering is not
/// visually verified.
/// </summary>
public sealed class LocalizationTests : IDisposable
{
    private readonly MemoryLanguageStore _store = new();

    public LocalizationTests()
    {
        // Keep tests deterministic: the service resolves "system" to English here.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    [Fact]
    public async Task SetLanguage_RaisesEvent_AndUpdatesLiveSource()
    {
        var service = CreateService();
        await service.InitializeAsync();
        LocalizationSource.Initialize(service);

        var source = LocalizationSource.Current;
        var itemNotifications = 0;
        var languageEvents = 0;
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                itemNotifications++;
            }
        };
        service.LanguageChanged += (_, _) => languageEvents++;

        Assert.Equal("Language", source["Settings.Language"]);

        await service.SetLanguageAsync("fr");

        Assert.Equal(1, languageEvents);
        Assert.Equal(1, itemNotifications);
        Assert.Equal("Langue", source["Settings.Language"]);
        Assert.Equal("fr", service.CurrentLanguage);
    }

    [Fact]
    public async Task Arabic_IsRightToLeft_AndUsesEuropeanDigits()
    {
        var service = CreateService();
        await service.InitializeAsync();
        LocalizationSource.Initialize(service);

        await service.SetLanguageAsync("ar");

        Assert.True(service.IsRtl());
        Assert.Equal(FlowDirection.RightToLeft, service.GetFlowDirection());
        Assert.Equal(FlowDirection.RightToLeft, LocalizationSource.Current.FlowDirection);
        Assert.Equal(NumberSubstitutionMethod.European, service.DigitsMethod);

        await service.SetLanguageAsync("es");

        Assert.False(service.IsRtl());
        Assert.Equal(FlowDirection.LeftToRight, service.GetFlowDirection());
        Assert.Equal(FlowDirection.LeftToRight, LocalizationSource.Current.FlowDirection);
    }

    [Fact]
    public async Task SetLanguage_UpdatesCurrentCultureAndUiCulture()
    {
        var service = CreateService();
        await service.InitializeAsync();

        await service.SetLanguageAsync("fr");

        Assert.Equal("fr", CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
        Assert.Equal("fr", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public async Task LanguageChoice_PersistsAndReloads()
    {
        var first = CreateService();
        await first.InitializeAsync();
        await first.SetLanguageAsync("es");

        Assert.Equal("es", _store.Load());

        var second = CreateService();
        await second.InitializeAsync();

        Assert.Equal("es", second.CurrentLanguage);
    }

    [Fact]
    public async Task Initialize_UsesWindowsLanguage_WhenSupported_ElseEnglish()
    {
        var french = new LocalizationService(
            new MemoryLanguageStore(),
            () => CultureInfo.GetCultureInfo("fr-CA"));
        await french.InitializeAsync();
        Assert.Equal("fr", french.CurrentLanguage);

        var japanese = new LocalizationService(
            new MemoryLanguageStore(),
            () => CultureInfo.GetCultureInfo("ja-JP"));
        await japanese.InitializeAsync();
        Assert.Equal("en", japanese.CurrentLanguage);
    }

    [Fact]
    public async Task MissingTranslation_FallsBackToEnglish()
    {
        // English has the key; French does not. The service must return English text
        // rather than the raw key.
        var service = new LocalizationService(
            new MemoryLanguageStore(),
            () => CultureInfo.GetCultureInfo("en-US"),
            (key, culture) => key == "Only.English" && culture.TwoLetterISOLanguageName == "en"
                ? "English text"
                : null);
        await service.InitializeAsync();
        await service.SetLanguageAsync("fr");

        Assert.Equal("English text", service.GetString("Only.English"));
    }

    [Fact]
    public async Task MissingKey_DebugShowsBracketedKey()
    {
        var service = CreateService();
        await service.InitializeAsync();

        var result = service.GetString("This.Key.Does.Not.Exist");

#if DEBUG
        Assert.Equal("[This.Key.Does.Not.Exist]", result);
#else
        Assert.Equal("This.Key.Does.Not.Exist", result);
#endif
    }

    [Fact]
    public async Task FormatArguments_AreAppliedPerLanguage()
    {
        var service = CreateService();
        await service.InitializeAsync();

        Assert.Equal("Version 0.4.0", service.GetString("Common.Version", "0.4.0"));

        await service.SetLanguageAsync("ar");
        Assert.Equal("الإصدار 0.4.0", service.GetString("Common.Version", "0.4.0"));
    }

    [Fact]
    public async Task SettingsViewModel_RerendersComposedText_OnLanguageChange()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var viewModel = new SettingsViewModel(new StubSettingsService(), localization: service);

        Assert.Equal("No API key configured", viewModel.ApiKeyStatusText);
        Assert.Equal("AI data-sharing consent: not granted", viewModel.AiConsentStatusText);
        Assert.Equal("Show", viewModel.ShowHideKeyText);

        await service.SetLanguageAsync("fr");

        Assert.Equal("Aucune clé API configurée", viewModel.ApiKeyStatusText);
        Assert.Equal("Consentement au partage de données avec l'IA : non accordé", viewModel.AiConsentStatusText);
        Assert.Equal("Afficher", viewModel.ShowHideKeyText);
    }

    [Fact]
    public async Task OnboardingViewModel_RerendersStepContent_OnLanguageChange()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var viewModel = new OnboardingViewModel(service);

        Assert.Equal("Step 1 of 3", viewModel.StepLabel);
        Assert.Equal("Welcome to DraftEngine", viewModel.StepTitle);
        Assert.Equal("Next", viewModel.PrimaryButtonText);

        await service.SetLanguageAsync("fr");

        Assert.Equal("Étape 1 sur 3", viewModel.StepLabel);
        Assert.Equal("Bienvenue dans DraftEngine", viewModel.StepTitle);
        Assert.Equal("Suivant", viewModel.PrimaryButtonText);
        Assert.Contains("contrat", viewModel.StepDescription);
    }

    [Fact]
    public async Task ContractRow_LocalizesStatusTitleAndAutomation()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var contract = new Contract
        {
            Title = string.Empty,
            Status = ContractStatus.Exported,
            UpdatedAt = new DateTime(2026, 1, 2, 15, 4, 0, DateTimeKind.Local)
        };

        var english = new ContractRowViewModel(contract, service);
        Assert.Equal("Untitled contract", english.Title);
        Assert.Equal("Exported", english.StatusText);
        Assert.Equal("Open contract Untitled contract", english.OpenAutomationName);

        await service.SetLanguageAsync("fr");
        var french = new ContractRowViewModel(contract, service);

        Assert.Equal("Contrat sans titre", french.Title);
        Assert.Equal("Exporté", french.StatusText);
        Assert.Equal("Ouvrir le contrat Contrat sans titre", french.OpenAutomationName);
        Assert.Contains("à", french.UpdatedAtText);
    }

    [Fact]
    public async Task ContractsList_RerendersRowsAndFilterLabels_OnLanguageChange()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var contract = new Contract
        {
            Id = 1,
            Title = "Acme NDA",
            ContractType = "Mutual NDA",
            Status = ContractStatus.Generated,
            EffectiveDate = DateTime.SpecifyKind(new DateTime(2026, 1, 2), DateTimeKind.Local),
            UpdatedAt = DateTime.SpecifyKind(new DateTime(2026, 1, 2), DateTimeKind.Local),
            TermMonths = 1
        };

        var viewModel = new ContractsListViewModel(
            new StubContractListService(contract),
            new StubContractEditService(),
            new StubNavigationService(),
            new StubDialogService(),
            service);

        Assert.Equal("Contracts", viewModel.PageTitle);
        Assert.Equal("All", viewModel.TypeFilters[0].DisplayName);
        Assert.Equal("Recently updated", viewModel.SortOptions[0].DisplayName);
        Assert.Single(viewModel.Contracts);
        Assert.Equal("Generated", viewModel.Contracts[0].StatusText);
        Assert.Equal("1 month", viewModel.Contracts[0].TermMonthsText);
        Assert.Equal("Jan 2, 2026", viewModel.Contracts[0].UpdatedDateText);
        Assert.Equal("Open contract Acme NDA", viewModel.Contracts[0].OpenAutomationName);

        await service.SetLanguageAsync("fr");

        Assert.Equal("Contrats", viewModel.PageTitle);
        Assert.Equal("Tous", viewModel.TypeFilters[0].DisplayName);
        Assert.Equal("Récemment mis à jour", viewModel.SortOptions[0].DisplayName);
        Assert.Single(viewModel.Contracts);
        Assert.Equal("Généré", viewModel.Contracts[0].StatusText);
        Assert.Equal("1 mois", viewModel.Contracts[0].TermMonthsText);
        Assert.Equal("Supprimer le contrat Acme NDA", viewModel.Contracts[0].DeleteAutomationName);
        Assert.Equal("2 janv. 2026", viewModel.Contracts[0].UpdatedDateText);
    }

    [Fact]
    public async Task NewContractDynamicLabels_RerenderWhenLanguageChanges()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var field = new IntakeFieldViewModel(
            "Party1", "Fallback", IntakeFieldKind.Text, isRequired: true, localization: service);
        var step = new WizardStepViewModel(1, "Wizard.Step.Type", service);

        Assert.Equal("Party 1 name", field.Label);
        Assert.Equal("Type", step.Label);

        await service.SetLanguageAsync("fr");
        field.RefreshLocalization();
        step.RefreshLocalization();

        Assert.Equal("Nom de la partie 1", field.Label);
        Assert.Equal("Type", step.Label);
    }

    [Fact]
    public async Task NewContractSummaryUsesLocalizedFieldLabels()
    {
        var service = CreateService();
        await service.InitializeAsync();
        await service.SetLanguageAsync("fr");

        Assert.Equal("Nom de la partie 1", service.GetString("Wizard.Field.Party1"));
        Assert.Equal("Contrat de prestataire indépendant",
            service.GetString("Wizard.Type.IndependentContractorAgreement.Name"));
        await service.SetLanguageAsync("es");
        Assert.Equal("Nombre de la parte 1", service.GetString("Wizard.Field.Party1"));
        Assert.Equal("Acuerdo de contratista independiente",
            service.GetString("Wizard.Type.IndependentContractorAgreement.Name"));
    }

    [Fact]
    public void RequiredNumberField_ValidatesInvariantIntegerInput()
    {
        var field = new IntakeFieldViewModel(
            "TermMonths", "Term", IntakeFieldKind.Number, isRequired: true);

        field.Value = "12";
        Assert.True(field.IsValid);

        field.Value = "١٢";
        Assert.False(field.IsValid);
    }

    private LocalizationService CreateService() =>
        new(_store, () => CultureInfo.GetCultureInfo("en-US"));

    public void Dispose()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    private sealed class StubSettingsService : DraftEngine.App.Services.Settings.IAppSettingsService
    {
        public DraftEngine.App.Services.Settings.AppSettings Current { get; } = new();

        public event Action? SettingsChanged;

        public Task<DraftEngine.App.Services.Settings.AppSettings> ReloadAsync() => Task.FromResult(Current);

        public Task SaveAsync(DraftEngine.App.Services.Settings.AppSettings settings)
        {
            SettingsChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task<DraftEngine.Core.Interfaces.IContractAiService?> CreateAiServiceAsync() =>
            Task.FromResult<DraftEngine.Core.Interfaces.IContractAiService?>(null);

        public DraftEngine.Core.Interfaces.IContractAiService? CreateAiService(string apiKey, string model) => null;
    }

    private sealed class StubContractListService : DraftEngine.Core.Interfaces.IContractService
    {
        private readonly List<Contract> _contracts;

        public StubContractListService(params Contract[] contracts) => _contracts = contracts.ToList();

        public Task<Contract> CreateAsync(Contract contract) => Task.FromResult(contract);

        public Task<Contract?> GetByIdAsync(int id) =>
            Task.FromResult(_contracts.FirstOrDefault(contract => contract.Id == id));

        public Task<List<Contract>> GetAllAsync(int userId) => Task.FromResult(_contracts.ToList());

        public Task<Contract?> UpdateAsync(Contract contract) => Task.FromResult<Contract?>(contract);

        public Task DeleteAsync(int id)
        {
            _contracts.RemoveAll(contract => contract.Id == id);
            return Task.CompletedTask;
        }

        public Task<string> GenerateContractAsync(int contractId, List<int> selectedClauseIds, bool useAiPolish = false) =>
            Task.FromResult(string.Empty);

        public Task<string> ApplyChatEditAsync(int contractId, string userInstruction) =>
            Task.FromResult(string.Empty);

        public Task<IReadOnlyList<DraftEngine.Core.Models.ContractVersion>> GetVersionsAsync(int contractId) =>
            Task.FromResult<IReadOnlyList<DraftEngine.Core.Models.ContractVersion>>(
                Array.Empty<DraftEngine.Core.Models.ContractVersion>());

        public Task<string> RevertToVersionAsync(int contractId, int versionId) =>
            Task.FromResult(string.Empty);
    }

    private sealed class StubContractEditService : DraftEngine.Core.Interfaces.IContractEditService
    {
        public Task<Contract?> DuplicateContractAsync(int sourceContractId, string newTitle) =>
            Task.FromResult<Contract?>(new Contract { Id = sourceContractId + 1, Title = newTitle });

        public Task<List<Contract>> GetArchivedContractsAsync(int userId) => Task.FromResult(new List<Contract>());

        public Task ArchiveContractAsync(int contractId) => Task.CompletedTask;

        public Task<bool> RestoreContractAsync(int contractId) => Task.FromResult(false);

        public Task PermanentDeleteContractAsync(int contractId) => Task.CompletedTask;

        public Task<Contract?> UpdateContractAsync(int contractId, Action<Contract> mutate) =>
            throw new NotSupportedException();
    }

    private sealed class StubNavigationService : DraftEngine.App.Services.INavigationService
    {
        public event Action<object?>? CurrentViewModelChanged
        {
            add { }
            remove { }
        }

        public object? CurrentViewModel => null;

        public string CurrentKey => string.Empty;

        public bool Navigate(string key, object? parameter = null) => true;
    }

    private sealed class StubDialogService : DraftEngine.App.Services.IWizardDialogService
    {
        public void ShowError(string message, string title = "DraftEngine")
        {
        }

        public bool Confirm(
            string message,
            string title = "DraftEngine",
            MessageBoxImage icon = MessageBoxImage.Warning) => false;
    }

    private sealed class MemoryLanguageStore : ILanguagePreferenceStore
    {
        private string? _value;

        public string? Load() => _value;

        public void Save(string languageCode) => _value = languageCode;
    }
}
