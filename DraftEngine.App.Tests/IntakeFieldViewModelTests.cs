using System.Globalization;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.ViewModels.NewContract;
using DraftEngine.Core.Data;

namespace DraftEngine.App.Tests;

/// <summary>
/// The wizard's jurisdiction field: localized country search with type-ahead, recent
/// selections first, the Windows-region default and the subdivision rules.
/// </summary>
public sealed class IntakeFieldViewModelTests
{
    private static readonly ILocalizationService Localization = new LocalizationService();

    [Fact]
    public void Jurisdiction_DefaultsToConfiguredCountryAndSubdivision()
    {
        var field = new IntakeFieldViewModel(
            "GoverningLaw", "Governing law", IntakeFieldKind.Jurisdiction, isRequired: true,
            localization: Localization, defaultCountryCode: "US", defaultSubdivision: "California");

        Assert.Equal("US", field.CountryCode);
        Assert.Equal("California", field.Subdivision);
        Assert.Equal(51, field.Subdivisions.Count);
        Assert.True(field.IsValid);
    }

    [Fact]
    public void Jurisdiction_UsesFreeTextSubdivision_ForCountriesWithoutAList()
    {
        var field = new IntakeFieldViewModel(
            "GoverningLaw", "Governing law", IntakeFieldKind.Jurisdiction, isRequired: true,
            localization: Localization, defaultCountryCode: "FR");

        Assert.Empty(field.Subdivisions);
        Assert.True(field.UsesFreeTextSubdivision);
        Assert.Equal("FR", field.CountryCode);
    }

    [Fact]
    public void CountrySearch_IsDiacriticInsensitive_AndMatchesLocalizedNames()
    {
        var field = new IntakeFieldViewModel(
            "GoverningLaw", "Governing law", IntakeFieldKind.Jurisdiction, isRequired: true,
            localization: Localization, defaultCountryCode: "US");

        field.CountrySearch = "cote";

        Assert.Contains(field.FilteredCountries, country => country.Code == "CI");
    }

    [Fact]
    public void RecentCountries_AreOfferedFirst()
    {
        var field = new IntakeFieldViewModel(
            "GoverningLaw", "Governing law", IntakeFieldKind.Jurisdiction, isRequired: true,
            localization: Localization, defaultCountryCode: "US", recentCountryCodes: new[] { "FR", "DE" });

        Assert.Equal("FR", field.FilteredCountries[0].Code);
        Assert.Equal("DE", field.FilteredCountries[1].Code);
        Assert.Contains(field.FilteredCountries, country => country.Code == "US");
    }

    [Fact]
    public void EffectiveValue_IsAStandaloneNounPhrase()
    {
        var field = new IntakeFieldViewModel(
            "GoverningLaw", "Governing law", IntakeFieldKind.Jurisdiction, isRequired: true,
            localization: Localization, defaultCountryCode: "US", defaultSubdivision: "California");

        // Other tests change the process culture; pin English for this assertion.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en");
            Assert.Equal(
                JurisdictionResolver.Compose("US", "California", "en"),
                field.EffectiveValue);
            Assert.Contains("United States", field.EffectiveValue);
            Assert.Contains("California", field.EffectiveValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Jurisdiction_IsInvalidWithoutACountry()
    {
        var field = new IntakeFieldViewModel(
            "GoverningLaw", "Governing law", IntakeFieldKind.Jurisdiction, isRequired: true,
            localization: Localization, defaultCountryCode: "US");

        field.SelectedCountry = null;

        Assert.False(field.IsValid);
    }

    [Fact]
    public void CountryField_UsesTheIsoCodeAsItsValue()
    {
        var field = new IntakeFieldViewModel(
            "VenueCountry", "Courts country", IntakeFieldKind.Country, isRequired: false,
            localization: Localization, defaultCountryCode: "CA");

        Assert.Equal("CA", field.EffectiveValue);

        field.SelectedCountry = CountryCatalog.Find("FR");
        Assert.Equal("FR", field.EffectiveValue);
        Assert.Equal("FR", field.CountryCode);
    }
}
