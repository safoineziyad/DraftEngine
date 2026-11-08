using System.Globalization;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.ViewModels;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DraftEngine.App.Tests;

public sealed class ContractDetailLocalizationTests : IClassFixture<StartupFixture>, IDisposable
{
    private readonly StartupFixture _fixture;
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;
    private readonly LocalizationService _localization = new(
        new MemoryLanguageStore(),
        () => CultureInfo.GetCultureInfo("en-US"));

    public ContractDetailLocalizationTests(StartupFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ContractDetailAndHistoryRows_RerenderAfterLanguageChange()
    {
        await _localization.InitializeAsync();
        var services = _fixture.Provider;
        using var viewModel = new ContractDetailViewModel(
            services.GetRequiredService<IContractService>(),
            services.GetRequiredService<IContractEditService>(),
            services.GetRequiredService<IFieldService>(),
            services.GetRequiredService<IClauseService>(),
            services.GetRequiredService<IContractTypeRegistry>(),
            services.GetRequiredService<IChatService>(),
            services.GetRequiredService<IDocxBuilder>(),
            services.GetRequiredService<IPdfBuilder>(),
            services.GetRequiredService<IDocumentExportService>(),
            services.GetRequiredService<INavigationService>(),
            services.GetRequiredService<IWizardDialogService>(),
            services.GetRequiredService<IAppSettingsService>(),
            services.GetRequiredService<IExportHistoryService>(),
            _localization);

        var contractId = _fixture.SeedContract();
        await viewModel.LoadCommand.ExecuteAsync(contractId);

        Assert.Equal("Contract detail", viewModel.PageTitleFallback);
        Assert.Equal("Mutual NDA", viewModel.ContractTypeDisplayName);
        Assert.Equal("Draft", viewModel.ContractStatusDisplayName);
        Assert.Equal("Version history", viewModel.VersionHistoryHeader);

        var version = new ContractVersionRowViewModel(new()
        {
            VersionNumber = 2,
            Reason = "AI edit",
            CreatedAt = new DateTime(2026, 1, 2, 15, 4, 0, DateTimeKind.Utc)
        }, _localization);
        Assert.StartsWith("AI edit ·", version.DetailsLabel);

        var export = new ExportHistoryRowViewModel(
            new()
            {
                FilePath = "C:\\exports\\agreement.pdf",
                FileType = FileType.Pdf,
                GeneratedAt = new DateTime(2026, 1, 2, 15, 4, 0, DateTimeKind.Local)
            },
            ExportFileStatus.Missing,
            _localization);
        Assert.Equal("File moved or deleted", export.StatusLabel);

        await _localization.SetLanguageAsync("fr");
        export.RefreshLocalization();

        Assert.Equal("Détails du contrat", viewModel.PageTitleFallback);
        Assert.Equal("Accord de confidentialité réciproque", viewModel.ContractTypeDisplayName);
        Assert.Equal("Brouillon", viewModel.ContractStatusDisplayName);
        Assert.Equal("Historique des versions", viewModel.VersionHistoryHeader);
        Assert.Equal("Fichier déplacé ou supprimé", export.StatusLabel);
        version.RefreshLocalization();
        Assert.StartsWith("Modification par IA ·", version.DetailsLabel);

        await viewModel.LoadCommand.ExecuteAsync(int.MaxValue);
        Assert.Equal("Ce contrat n'existe plus. Il a peut-être été supprimé.", viewModel.LoadFailedMessage);

        await _localization.SetLanguageAsync("en");
        Assert.Equal("This contract no longer exists. It may have been deleted.", viewModel.LoadFailedMessage);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }

    private sealed class MemoryLanguageStore : ILanguagePreferenceStore
    {
        private string? _value;

        public string? Load() => _value;

        public void Save(string languageCode) => _value = languageCode;
    }
}
