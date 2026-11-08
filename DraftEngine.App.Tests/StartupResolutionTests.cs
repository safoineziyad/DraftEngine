using System.Xml.Linq;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.ViewModels;
using DraftEngine.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Startup safety net: everything the app resolves at runtime must be constructible, every
/// navigation key must have a view template, and every page must load against an empty and a
/// seeded database without throwing.
/// </summary>
public sealed class StartupResolutionTests : IClassFixture<StartupFixture>
{
    private readonly StartupFixture _fixture;

    public StartupResolutionTests(StartupFixture fixture)
    {
        _fixture = fixture;
    }

    public static IEnumerable<object[]> PageViewModels => new[]
    {
        typeof(DashboardViewModel),
        typeof(NewContractViewModel),
        typeof(ContractsListViewModel),
        typeof(ContractDetailViewModel),
        typeof(SettingsViewModel),
        typeof(ClauseManagerViewModel),
        typeof(ArchivedContractsViewModel),
        typeof(AboutViewModel),
        typeof(MainViewModel),
    }.Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(PageViewModels))]
    public void Container_ResolvesEveryPageViewModel(Type viewModelType)
    {
        var viewModel = _fixture.Provider.GetRequiredService(viewModelType);

        Assert.NotNull(viewModel);
        Assert.IsType(viewModelType, viewModel);
    }

    public static IEnumerable<object[]> Services => new[]
    {
        typeof(INavigationService),
        typeof(IContractService),
        typeof(IClauseService),
        typeof(IFieldService),
        typeof(IContractTypeRegistry),
        typeof(IAiConsentService),
        typeof(IAiEndpointSettings),
        typeof(IAiRedactionService),
        typeof(IContractAiService),
        typeof(IContractEditService),
        typeof(IExportHistoryService),
        typeof(IWizardDraftService),
        typeof(IWizardDialogService),
        typeof(IAppSettingsService),
        typeof(IDocumentBuilder),
        typeof(IDocxBuilder),
        typeof(IPdfBuilder),
        typeof(IDocumentExportService),
        typeof(DraftEngine.App.Services.Licensing.ILicenseService),
        typeof(DraftEngine.App.Services.Updates.IUpdateCheckService),
        typeof(DraftEngine.App.Services.Updates.IUpdateService),
    }.Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(Services))]
    public void Container_ResolvesEveryRegisteredService(Type serviceType)
    {
        Assert.NotNull(_fixture.Provider.GetRequiredService(serviceType));
    }

    [Fact]
    public void EveryNavigationKey_HasAViewModelAndADataTemplate()
    {
        var appXaml = StartupFixture.LoadAppXaml();
        var ns = appXaml.Root!.GetDefaultNamespace();
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var templateTypes = appXaml.Descendants(ns + "DataTemplate")
            .Select(element => element.Attribute("DataType")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Replace("{x:Type vm:", string.Empty).TrimEnd('}'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(templateTypes);
        Assert.Equal(NavigationService.Pages.Count, templateTypes.Count);

        foreach (var (key, viewModelType) in NavigationService.Pages)
        {
            Assert.False(string.IsNullOrWhiteSpace(key));
            Assert.Contains(viewModelType.Name, templateTypes);
        }
    }

    [Fact]
    public async Task EveryPage_LoadsAgainstAnEmptyDatabase()
    {
        var navigation = _fixture.Provider.GetRequiredService<INavigationService>();

        foreach (var key in NavigationService.Pages.Keys)
        {
            var navigated = navigation.Navigate(key);
            Assert.True(navigated, $"Navigation key '{key}' should be known.");
            await Task.Delay(150);
        }
    }

    [Fact]
    public async Task EveryPage_LoadsAgainstASeededDatabase()
    {
        var navigation = _fixture.Provider.GetRequiredService<INavigationService>();
        var contractId = _fixture.SeedContract();

        foreach (var key in NavigationService.Pages.Keys)
        {
            object? parameter = key == "contract-detail" ? contractId : null;
            var navigated = navigation.Navigate(key, parameter);
            Assert.True(navigated, $"Navigation key '{key}' should be known.");
            await Task.Delay(150);
        }

        // Navigate the detail page again to assert the seeded contract actually loaded.
        Assert.True(navigation.Navigate("contract-detail", contractId));
        var detail = Assert.IsType<ContractDetailViewModel>(navigation.CurrentViewModel);
        Assert.NotNull(detail);
    }
}
