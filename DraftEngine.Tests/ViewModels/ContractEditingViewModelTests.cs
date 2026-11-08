using System.Windows;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.ViewModels;
using DraftEngine.App.ViewModels.NewContract;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Models.ContractTypes;

namespace DraftEngine.Tests.ViewModels;

public class ContractEditingViewModelTests
{
    [Fact]
    public async Task SaveAndRegenerate_PersistsEditsThenSnapshotsPreviousTextWithSelectedClauses()
    {
        var contract = CreateContract();
        var activeClause = new Clause
        {
            Id = 12,
            ContractType = ContractTypeRegistry.MutualNdaId,
            Category = "Terms",
            Title = "Current clause",
            BodyTemplate = "Updated clause body",
            IsOptional = true
        };
        var archivedClause = new Clause
        {
            Id = 11,
            ContractType = ContractTypeRegistry.MutualNdaId,
            Category = "Archived",
            Title = "Previously attached clause",
            BodyTemplate = "Existing body",
            IsArchived = true
        };
        contract.ContractClauses.Add(new ContractClause
        {
            ContractId = contract.Id,
            ClauseId = archivedClause.Id,
            Selected = true,
            Clause = archivedClause
        });
        contract.ContractClauses.Add(new ContractClause
        {
            ContractId = contract.Id,
            ClauseId = activeClause.Id,
            Selected = false,
            Clause = activeClause
        });

        var calls = new List<string>();
        var contracts = new RecordingContractService(contract, calls);
        var edits = new RecordingContractEditService(contract, calls);
        var fields = new RecordingFieldService(contract, calls);
        var viewModel = CreateDetailViewModel(
            contracts,
            edits,
            fields,
            new StubClauseService(activeClause),
            new StubNavigationService());

        await viewModel.LoadCommand.ExecuteAsync(contract.Id);

        Assert.Equal("PREVIOUS DOCUMENT", viewModel.DocumentText);
        Assert.Contains(
            viewModel.ClauseGroups.SelectMany(group => group.Clauses),
            option => option.Id == archivedClause.Id && option.IsSelected);

        var activeOption = Assert.Single(
            viewModel.ClauseGroups.SelectMany(group => group.Clauses),
            option => option.Id == activeClause.Id);
        activeOption.IsSelected = true;
        var retainedArchivedOption = Assert.Single(
            viewModel.ClauseGroups.SelectMany(group => group.Clauses),
            option => option.Id == archivedClause.Id);
        retainedArchivedOption.IsSelected = false;
        viewModel.EditTitle = "  Revised NDA  ";
        var firstParty = Assert.Single(viewModel.IntakeFields, field => field.Key == "Party1");
        firstParty.Value = "Revised Discloser";

        Assert.Equal("PREVIOUS DOCUMENT", viewModel.DocumentText);
        await viewModel.RegenerateCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "metadata", "intake", "generate" }, calls);
        Assert.Equal(new[] { activeClause.Id }, contracts.GeneratedClauseIds);
        Assert.Equal("Revised NDA", contract.Title);
        Assert.Contains(contract.ContractFields, field =>
            field.Key == "Party1" && field.Value == "Revised Discloser");
        Assert.Equal("REGENERATED DOCUMENT", viewModel.DocumentText);
        var snapshot = Assert.Single(await contracts.GetVersionsAsync(contract.Id));
        Assert.Equal("PREVIOUS DOCUMENT", snapshot.Text);
        Assert.Equal(DraftEngine.Core.Services.ContractService.RegenerationReason, snapshot.Reason);
    }

    [Fact]
    public async Task DetailDuplicate_UsesEditServiceAndOpensTheCopy()
    {
        var contract = CreateContract();
        var calls = new List<string>();
        var edits = new RecordingContractEditService(contract, calls);
        var navigation = new StubNavigationService();
        var viewModel = CreateDetailViewModel(
            new RecordingContractService(contract, calls),
            edits,
            new RecordingFieldService(contract, calls),
            new StubClauseService(),
            navigation);
        viewModel.Contract = contract;

        await viewModel.DuplicateContractCommand.ExecuteAsync(null);

        Assert.Equal((contract.Id, $"Copy of {contract.Title}"), edits.DuplicateRequest);
        Assert.Equal(("contract-detail", (object?)(contract.Id + 1)), navigation.LastNavigation);
    }

    [Fact]
    public async Task ListDuplicate_UsesEditServiceAndOpensTheCopy()
    {
        var contract = CreateContract();
        var calls = new List<string>();
        var edits = new RecordingContractEditService(contract, calls);
        var navigation = new StubNavigationService();
        var viewModel = new ContractsListViewModel(
            new RecordingContractService(contract, calls),
            edits,
            navigation,
            new StubDialogService());

        await viewModel.DuplicateCommand.ExecuteAsync(contract);

        Assert.Equal((contract.Id, $"Copy of {contract.Title}"), edits.DuplicateRequest);
        Assert.Equal(("contract-detail", (object?)(contract.Id + 1)), navigation.LastNavigation);
    }

    private static ContractDetailViewModel CreateDetailViewModel(
        IContractService contracts,
        IContractEditService edits,
        IFieldService fields,
        IClauseService clauses,
        INavigationService navigation) =>
        new(
            contracts,
            edits,
            fields,
            clauses,
            ContractTypeRegistry.Instance,
            new StubChatService(),
            new StubDocxBuilder(),
            new StubPdfBuilder(),
            new StubDocumentExportService(),
            navigation,
            new StubDialogService(),
            new StubSettingsService());

    private static Contract CreateContract()
    {
        var contract = new Contract
        {
            Id = 5,
            UserId = 1,
            Title = "Original NDA",
            ContractType = ContractTypeRegistry.MutualNdaId,
            ClientName = "Receiving Party",
            ClientEmail = "client@example.com",
            PartiesJson = "[{\"name\":\"Disclosing Party\",\"address\":\"1 Main St\"},{\"name\":\"Receiving Party\",\"address\":\"2 Main St\"}]",
            EffectiveDate = new DateTime(2025, 1, 1),
            GoverningLaw = "California",
            TermMonths = 12,
            AssembledText = "PREVIOUS DOCUMENT",
            ContractFields = new List<ContractField>
            {
                new() { Key = "Party1", Value = "Disclosing Party" },
                new() { Key = "Party1Address", Value = "1 Main St" },
                new() { Key = "Party2", Value = "Receiving Party" },
                new() { Key = "Party2Address", Value = "2 Main St" },
                new() { Key = "EffectiveDate", Value = "January 1, 2025" },
                new() { Key = "GoverningLaw", Value = "California" },
                new() { Key = "TermMonths", Value = "12" }
            }
        };
        return contract;
    }

    private sealed class RecordingContractService : IContractService
    {
        private readonly Contract _contract;
        private readonly List<string> _calls;
        private readonly List<ContractVersion> _versions = new();

        public RecordingContractService(Contract contract, List<string> calls)
        {
            _contract = contract;
            _calls = calls;
        }

        public List<int> GeneratedClauseIds { get; private set; } = new();

        public Task<Contract> CreateAsync(Contract contract) => Task.FromResult(contract);
        public Task<Contract?> GetByIdAsync(int id) => Task.FromResult<Contract?>(id == _contract.Id ? _contract : null);
        public Task<List<Contract>> GetAllAsync(int userId) => Task.FromResult(new List<Contract> { _contract });
        public Task<Contract?> UpdateAsync(Contract contract) => Task.FromResult<Contract?>(contract);
        public Task DeleteAsync(int id) => Task.CompletedTask;

        public Task<string> GenerateContractAsync(int contractId, List<int> selectedClauseIds, bool useAiPolish = false)
        {
            _calls.Add("generate");
            GeneratedClauseIds = selectedClauseIds;
            _versions.Add(new ContractVersion
            {
                ContractId = contractId,
                VersionNumber = _versions.Count + 1,
                Reason = DraftEngine.Core.Services.ContractService.RegenerationReason,
                Text = _contract.AssembledText
            });
            _contract.AssembledText = "REGENERATED DOCUMENT";
            return Task.FromResult(_contract.AssembledText);
        }

        public Task<string> ApplyChatEditAsync(int contractId, string userInstruction) => Task.FromResult(string.Empty);
        public Task<IReadOnlyList<ContractVersion>> GetVersionsAsync(int contractId) =>
            Task.FromResult<IReadOnlyList<ContractVersion>>(_versions.ToList());
        public Task<string> RevertToVersionAsync(int contractId, int versionId) => Task.FromResult(string.Empty);
    }

    private sealed class RecordingContractEditService : IContractEditService
    {
        private readonly Contract _contract;
        private readonly List<string> _calls;

        public RecordingContractEditService(Contract contract, List<string> calls)
        {
            _contract = contract;
            _calls = calls;
        }

        public (int ContractId, string Title)? DuplicateRequest { get; private set; }

        public Task<Contract?> DuplicateContractAsync(int sourceContractId, string newTitle)
        {
            DuplicateRequest = (sourceContractId, newTitle);
            return Task.FromResult<Contract?>(new Contract { Id = sourceContractId + 1, Title = newTitle });
        }

        public Task ArchiveContractAsync(int contractId) => Task.CompletedTask;
        public Task<List<Contract>> GetArchivedContractsAsync(int userId) => Task.FromResult(new List<Contract>());
        public Task<bool> RestoreContractAsync(int contractId) => Task.FromResult(false);
        public Task PermanentDeleteContractAsync(int contractId) => Task.CompletedTask;

        public Task<Contract?> UpdateContractAsync(int contractId, Action<Contract> mutate)
        {
            _calls.Add("metadata");
            mutate(_contract);
            return Task.FromResult<Contract?>(_contract);
        }
    }

    private sealed class RecordingFieldService : IFieldService
    {
        private readonly Contract _contract;
        private readonly List<string> _calls;

        public RecordingFieldService(Contract contract, List<string> calls)
        {
            _contract = contract;
            _calls = calls;
        }

        public Task<Dictionary<string, string>> MapFieldsAsync(int contractId, Dictionary<string, string> formValues)
        {
            _calls.Add("intake");
            foreach (var pair in formValues)
            {
                var existing = _contract.ContractFields.FirstOrDefault(field =>
                    string.Equals(field.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    _contract.ContractFields.Add(new ContractField { ContractId = contractId, Key = pair.Key, Value = pair.Value });
                }
                else
                {
                    existing.Value = pair.Value;
                }
            }

            return Task.FromResult(formValues);
        }

        public string ReplacePlaceholders(string template, Dictionary<string, string> fieldValues) => template;
    }

    private sealed class StubClauseService : IClauseService
    {
        private readonly List<Clause> _clauses;

        public StubClauseService(params Clause[] clauses) => _clauses = clauses.ToList();

        public Task<List<Clause>> GetClausesByContractTypeAsync(string contractType) => Task.FromResult(_clauses);
        public Task<List<Clause>> GetDefaultSelectedClausesAsync(string contractType) => Task.FromResult(new List<Clause>());
    }

    private sealed class StubNavigationService : INavigationService
    {
        public event Action<object?>? CurrentViewModelChanged
        {
            add { }
            remove { }
        }
        public object? CurrentViewModel => null;
        public string CurrentKey => string.Empty;
        public (string Key, object? Parameter)? LastNavigation { get; private set; }

        public bool Navigate(string key, object? parameter = null)
        {
            LastNavigation = (key, parameter);
            return true;
        }
    }

    private sealed class StubChatService : IChatService
    {
        public Task<ChatMessage> AddMessageAsync(int contractId, MessageRole role, string content) =>
            Task.FromResult(new ChatMessage());
        public Task<List<ChatMessage>> GetMessagesAsync(int contractId) => Task.FromResult(new List<ChatMessage>());
    }

    private sealed class StubDocxBuilder : IDocxBuilder
    {
        public Task<string> BuildDocxAsync(Contract contract, string fullContractText, string outputPath) =>
            Task.FromResult(outputPath);
    }

    private sealed class StubPdfBuilder : IPdfBuilder
    {
        public Task<string> BuildPdfAsync(Contract contract, string fullContractText, string outputPath) =>
            Task.FromResult(outputPath);
    }

    private sealed class StubDocumentExportService : IDocumentExportService
    {
        public Task<GeneratedDocument> RecordExportAsync(int contractId, string filePath, FileType fileType) =>
            Task.FromResult(new GeneratedDocument());
    }

    private sealed class StubDialogService : IWizardDialogService
    {
        public void ShowError(string message, string title = "DraftEngine") { }
        public bool Confirm(string message, string title = "DraftEngine", MessageBoxImage icon = MessageBoxImage.Warning) => true;
    }

    private sealed class StubSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; } = new();
        public event Action? SettingsChanged { add { } remove { } }
        public Task<AppSettings> ReloadAsync() => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings) => Task.CompletedTask;
        public Task<IContractAiService?> CreateAiServiceAsync() => Task.FromResult<IContractAiService?>(null);
        public IContractAiService? CreateAiService(string apiKey, string model) => null;
    }
}
