using DraftEngine.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>Search must cover contract body text, not only title/client.</summary>
public sealed class ContractsSearchTests : IClassFixture<StartupFixture>
{
    private readonly StartupFixture _fixture;

    public ContractsSearchTests(StartupFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Search_FindsTextInsideTheContractBody()
    {
        _fixture.SeedContract();
        var viewModel = _fixture.Provider.GetRequiredService<ContractsListViewModel>();
        await Task.Delay(250);

        viewModel.SearchText = "two years";
        await Task.Delay(150);
        Assert.NotEmpty(viewModel.Contracts);

        viewModel.SearchText = "no-such-phrase-98765";
        await Task.Delay(150);
        Assert.Empty(viewModel.Contracts);
    }
}
