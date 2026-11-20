using DraftEngine.App.Services.Settings;
using DraftEngine.App.ViewModels;
using DraftEngine.App.ViewModels.NewContract;
using DraftEngine.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Verifies FIX 5:
/// - AppSettings defaults governing law to the user's region/country, never "California".
/// - NewContractViewModel defaults the governing law intake field to the configured country, never "California".
/// </summary>
public sealed class JurisdictionDefaultTests : IClassFixture<StartupFixture>
{
    private readonly StartupFixture _fixture;

    public JurisdictionDefaultTests(StartupFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void AppSettings_DefaultGoverningLaw_DoesNotDefaultToCalifornia()
    {
        var settings = new AppSettings();

        Assert.NotEqual("California", settings.DefaultGoverningLaw);
        Assert.Equal(CountryCatalog.DefaultCode(), settings.DefaultGoverningLawCountry);
        Assert.Equal(string.Empty, settings.DefaultGoverningLawSubdivision);
    }

    [Fact]
    public void NewContract_GoverningLawField_DefaultsToRegionCountry_NotCalifornia()
    {
        var vm = _fixture.Provider.GetRequiredService<NewContractViewModel>();

        // Step 1: select Mutual NDA and go to Step 2
        var mutualNda = vm.ContractTypes.First(t => t.Id == "MutualNda");
        vm.SelectContractTypeCommand.Execute(mutualNda);
        vm.NextCommand.Execute(null);

        var governingLawField = vm.IntakeFields.FirstOrDefault(f => f.Key == "GoverningLaw");
        Assert.NotNull(governingLawField);
        Assert.Equal(IntakeFieldKind.Jurisdiction, governingLawField.Kind);
        Assert.Equal(CountryCatalog.DefaultCode(), governingLawField.CountryCode);
        Assert.NotEqual("California", governingLawField.Subdivision);
    }
}
