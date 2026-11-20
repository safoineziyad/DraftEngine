using System.Globalization;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.ViewModels;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Verifies FIX 2: Contracts list headers and cells align properly, header texts are
/// localized across all supported languages, and the contract type column displays human-readable
/// display names (never raw enum, identifier, or class names).
/// </summary>
public sealed class ContractsListHeaderTests : IClassFixture<StartupFixture>
{
    private readonly StartupFixture _fixture;

    public ContractsListHeaderTests(StartupFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("en", "Title", "Contract type", "Status")]
    [InlineData("fr", "Titre", "Type de contrat", "Statut")]
    [InlineData("ar", "العنوان", "نوع العقد", "الحالة")]
    [InlineData("es", "Título", "Tipo de contrato", "Estado")]
    public async Task ColumnHeaders_AreLocalizedInAllSupportedLanguages(
        string lang,
        string expectedTitle,
        string expectedType,
        string expectedStatus)
    {
        var localization = _fixture.Provider.GetRequiredService<ILocalizationService>();
        await localization.SetLanguageAsync(lang);

        Assert.Equal(expectedTitle, localization.GetString("Contracts.ColumnTitle"));
        Assert.Equal(expectedType, localization.GetString("Contracts.ColumnType"));
        Assert.Equal(expectedStatus, localization.GetString("Contracts.ColumnStatus"));
        Assert.False(string.IsNullOrWhiteSpace(localization.GetString("Contracts.ColumnEffective")));
        Assert.False(string.IsNullOrWhiteSpace(localization.GetString("Contracts.ColumnTerm")));
        Assert.False(string.IsNullOrWhiteSpace(localization.GetString("Contracts.ColumnUpdated")));
    }

    [Theory]
    [InlineData("MutualNda", "Mutual NDA")]
    [InlineData("OneWayNda", "One-Way NDA")]
    [InlineData("IndependentContractorAgreement", "Independent Contractor Agreement")]
    [InlineData("ConsultingAgreement", "Consulting Agreement")]
    [InlineData("Mutual NDA", "Mutual NDA")]
    public void ContractRowViewModel_ContractType_AlwaysReturnsDisplayName_NeverRawIdOrClass(
        string rawType,
        string expectedDisplayName)
    {
        var localization = _fixture.Provider.GetRequiredService<ILocalizationService>();
        var contract = new Contract
        {
            Title = "Test Contract",
            ContractType = rawType,
            Status = ContractStatus.Draft,
            UpdatedAt = DateTime.UtcNow
        };

        var rowVm = new ContractRowViewModel(contract, localization);

        Assert.Equal(expectedDisplayName, rowVm.ContractType);
        Assert.DoesNotContain("DraftEngine", rowVm.ContractType);
        Assert.DoesNotContain("Enum", rowVm.ContractType);
    }

    [Fact]
    public void ContractRowViewModel_StatusText_ReturnsLocalizedStatus_NeverRawEnumName()
    {
        var localization = _fixture.Provider.GetRequiredService<ILocalizationService>();

        foreach (var status in new[] { ContractStatus.Draft, ContractStatus.Generated, ContractStatus.Exported })
        {
            var contract = new Contract
            {
                Title = "Test Contract",
                ContractType = "MutualNda",
                Status = status,
                UpdatedAt = DateTime.UtcNow
            };

            var rowVm = new ContractRowViewModel(contract, localization);

            Assert.False(string.IsNullOrWhiteSpace(rowVm.StatusText));
            Assert.DoesNotContain("DraftEngine", rowVm.StatusText);
        }
    }
}
