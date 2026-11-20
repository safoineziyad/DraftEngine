using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.Themes;
using DraftEngine.App.ViewModels;
using DraftEngine.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// FIX 1 audit: Every dropdown and picker in the app must expose localized display names,
/// never raw object text, and every ComboBox in XAML must use DisplayMemberPath or ItemTemplate
/// when binding to non-string collections.
/// </summary>
public sealed class DropdownAuditTests : IClassFixture<StartupFixture>
{
    private readonly StartupFixture _fixture;

    public DropdownAuditTests(StartupFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void SettingsViewModel_SupportedLanguages_ExposeLocalizedNativeNames()
    {
        var localization = _fixture.Provider.GetRequiredService<ILocalizationService>();
        var settingsService = _fixture.Provider.GetRequiredService<IAppSettingsService>();
        var themeService = _fixture.Provider.GetRequiredService<IThemeService>();

        var vm = new SettingsViewModel(settingsService, localization: localization, themeService: themeService);

        Assert.NotEmpty(vm.SupportedLanguages);
        foreach (var lang in vm.SupportedLanguages)
        {
            Assert.False(string.IsNullOrWhiteSpace(lang.NativeName));
            Assert.DoesNotContain("{ Id =", lang.NativeName);
            Assert.DoesNotContain("SupportedLanguage", lang.NativeName);
        }
    }

    [Fact]
    public void SettingsViewModel_ThemeOptions_ExposeLocalizedDisplayNames()
    {
        var localization = _fixture.Provider.GetRequiredService<ILocalizationService>();
        var settingsService = _fixture.Provider.GetRequiredService<IAppSettingsService>();
        var themeService = _fixture.Provider.GetRequiredService<IThemeService>();

        var vm = new SettingsViewModel(settingsService, localization: localization, themeService: themeService);

        Assert.NotEmpty(vm.ThemeOptions);
        foreach (var theme in vm.ThemeOptions)
        {
            Assert.False(string.IsNullOrWhiteSpace(theme.DisplayName));
            Assert.DoesNotContain("DraftEngine.App.", theme.DisplayName);
        }
    }

    [Fact]
    public void ContractsListViewModel_TypeFiltersAndSortOptions_ExposeDisplayNames()
    {
        var vm = _fixture.Provider.GetRequiredService<ContractsListViewModel>();

        Assert.NotEmpty(vm.TypeFilters);
        foreach (var option in vm.TypeFilters)
        {
            Assert.False(string.IsNullOrWhiteSpace(option.DisplayName));
            Assert.DoesNotContain("{ Id =", option.DisplayName);
            Assert.DoesNotContain("SelectOption", option.DisplayName);
        }

        Assert.NotEmpty(vm.SortOptions);
        foreach (var option in vm.SortOptions)
        {
            Assert.False(string.IsNullOrWhiteSpace(option.DisplayName));
            Assert.DoesNotContain("{ Id =", option.DisplayName);
            Assert.DoesNotContain("SelectOption", option.DisplayName);
        }
    }

    [Fact]
    public void AnalyzeViewModel_ContractTypes_ExposeDisplayNames()
    {
        var vm = _fixture.Provider.GetRequiredService<AnalyzeViewModel>();

        Assert.NotEmpty(vm.ContractTypes);
        foreach (var ct in vm.ContractTypes)
        {
            Assert.False(string.IsNullOrWhiteSpace(ct.DisplayName));
            Assert.DoesNotContain("{ Id =", ct.DisplayName);
            Assert.DoesNotContain("ContractTypeOption", ct.DisplayName);
        }
    }

    [Fact]
    public void AllXamlComboBoxes_HaveDisplayMemberPathOrItemTemplate_WhenBound()
    {
        // Known collections of primitive strings which display directly without DisplayMemberPath
        var allowedStringCollections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Choices",
            "ContractTypes",
            "CategoryFilters",
            "SourceFilters",
            "Subdivisions",
            "ModelOptions",
            "GoverningLawSubdivisions"
        };

        // Locate DraftEngine.App XAML files
        var viewsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "DraftEngine.App"));
        var xamlFiles = Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
            .ToList();

        Assert.NotEmpty(xamlFiles);

        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file);
            // Quick check for ComboBox tags
            var comboMatches = Regex.Matches(content, @"<ComboBox\b(?<attrs>[^>]*?)(?:/>|>(?<inner>[\s\S]*?)</ComboBox>)", RegexOptions.Compiled);

            foreach (Match match in comboMatches)
            {
                var attrs = match.Groups["attrs"].Value;
                var inner = match.Groups["inner"].Value;

                // Check ItemsSource binding
                var itemsSourceMatch = Regex.Match(attrs, @"ItemsSource=""\{Binding\s+([A-Za-z0-9_]+)[^}]*\}""");
                if (!itemsSourceMatch.Success)
                {
                    // Not a bound ItemsSource or static items
                    continue;
                }

                var boundProperty = itemsSourceMatch.Groups[1].Value;
                if (allowedStringCollections.Contains(boundProperty))
                {
                    // Primitive string collection is allowed without DisplayMemberPath
                    continue;
                }

                var hasDisplayMemberPath = Regex.IsMatch(attrs, @"DisplayMemberPath=""[^""]+""");
                var hasItemTemplate = inner.Contains("<ComboBox.ItemTemplate>") || attrs.Contains("ItemTemplate=");

                Assert.True(
                    hasDisplayMemberPath || hasItemTemplate,
                    $"ComboBox binding to '{boundProperty}' in {Path.GetFileName(file)} lacks DisplayMemberPath or ItemTemplate.");
            }
        }
    }
}
