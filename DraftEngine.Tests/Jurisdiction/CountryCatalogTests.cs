using System.Globalization;
using DraftEngine.Core.Data;

namespace DraftEngine.Tests.Jurisdiction;

public sealed class CountryCatalogTests
{
    [Fact]
    public void EveryCountry_HasNamesInAllFourLanguages_AndUniqueCodes()
    {
        Assert.True(CountryCatalog.All.Count >= 240,
            $"expected at least 240 countries, found {CountryCatalog.All.Count}");

        Assert.Equal(
            CountryCatalog.All.Count,
            CountryCatalog.All.Select(country => country.Code).Distinct(StringComparer.Ordinal).Count());

        foreach (var country in CountryCatalog.All)
        {
            foreach (var language in new[] { "en", "fr", "ar", "es" })
            {
                Assert.False(string.IsNullOrWhiteSpace(country.Name(language)),
                    $"{country.Code} has no {language} name");
            }
        }
    }

    [Fact]
    public void NamesAreLocalized_NotJustCopiedFromEnglish()
    {
        Assert.Equal("Allemagne", CountryCatalog.Name("DE", "fr"));
        Assert.Equal("ألمانيا", CountryCatalog.Name("DE", "ar"));
        Assert.Equal("Alemania", CountryCatalog.Name("DE", "es"));
        Assert.Equal("Germany", CountryCatalog.Name("DE", "en"));
    }

    [Fact]
    public void Sort_UsesEachCulturesCollation()
    {
        foreach (var language in new[] { "fr", "ar", "es" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            var sorted = CountryCatalog.Sort(CountryCatalog.All, culture);
            var names = sorted.Select(country => country.Name(language)).ToArray();

            var expected = names
                .OrderBy(name => name, StringComparer.Create(culture, ignoreCase: false))
                .ToArray();

            Assert.Equal(expected, names);
        }
    }

    [Fact]
    public void Search_IgnoresDiacritics_AndWorksInArabic()
    {
        var french = CultureInfo.GetCultureInfo("fr");
        Assert.Contains(CountryCatalog.Search("cote", french), country => country.Code == "CI");
        Assert.Contains(CountryCatalog.Search("Côte", french), country => country.Code == "CI");
        Assert.Contains(CountryCatalog.Search("etats-unis", french), country => country.Code == "US");

        var arabic = CultureInfo.GetCultureInfo("ar");
        Assert.Contains(CountryCatalog.Search("الإمارات", arabic), country => country.Code == "AE");
    }

    [Fact]
    public void Search_MatchesCodesAndWordStarts()
    {
        Assert.Contains(CountryCatalog.Search("ger", CultureInfo.GetCultureInfo("en")),
            country => country.Code == "DE");
        Assert.Contains(CountryCatalog.Search("GB", CultureInfo.GetCultureInfo("en")),
            country => country.Code == "GB");
        Assert.Contains(CountryCatalog.Search("king", CultureInfo.GetCultureInfo("en")),
            country => country.Code == "GB");
    }

    [Fact]
    public void DefaultCode_IsAlwaysBundled()
    {
        Assert.NotNull(CountryCatalog.Find(CountryCatalog.DefaultCode()));
    }

    [Fact]
    public void BuiltInSubdivisions_CoverOnlyTheCertainCountries()
    {
        Assert.Equal(51, SubdivisionCatalog.For("US").Count); // 50 states + DC
        Assert.Equal(13, SubdivisionCatalog.For("CA").Count);
        Assert.Equal(8, SubdivisionCatalog.For("AU").Count);
        Assert.Equal(4, SubdivisionCatalog.For("GB").Count);
        Assert.Empty(SubdivisionCatalog.For("FR"));
        Assert.True(SubdivisionCatalog.HasList("us"));
        Assert.False(SubdivisionCatalog.HasList("DE"));
    }

    [Theory]
    [InlineData("California", "US", "California")]
    [InlineData("Delaware", "US", "Delaware")]
    [InlineData("Ontario", "CA", "Ontario")]
    [InlineData("New South Wales", "AU", "New South Wales")]
    [InlineData("Scotland", "GB", "Scotland")]
    [InlineData("England and Wales", "GB", "England and Wales")]
    [InlineData("France", "FR", "")]
    [InlineData("Deutschland", "", "")]
    public void LegacyValues_MapWithoutLoss(string legacy, string country, string subdivision)
    {
        var mapped = JurisdictionResolver.TryMapLegacy(legacy, out var countryCode, out var mappedSubdivision);

        Assert.Equal(country.Length > 0, mapped);
        Assert.Equal(country, countryCode);
        Assert.Equal(subdivision, mappedSubdivision);
    }

    [Fact]
    public void Compose_ProducesAStandaloneNounPhrase()
    {
        Assert.Equal("United States — California",
            JurisdictionResolver.Compose("US", "California", "en"));
        Assert.Equal("États-Unis — Californie",
            JurisdictionResolver.Compose("US", "Californie", "fr"));
        Assert.Equal("France", JurisdictionResolver.Compose("FR", "", "en"));
    }
}
