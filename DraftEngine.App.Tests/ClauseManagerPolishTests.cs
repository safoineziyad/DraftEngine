using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.ViewModels;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Verifies FIX 4:
/// - ClauseManagerViewModel uses injected ILocalizationService for PageTitle, errors, dialogs, and filters.
/// - Filter dropdowns have visible labels in the UI.
/// - ListView has column headers (Title, Category, Source, Order, Optional/Default).
/// - Optional/Default is formatted cleanly with localized text, not raw string format.
/// </summary>
public sealed class ClauseManagerPolishTests : IClassFixture<StartupFixture>
{
    private readonly StartupFixture _fixture;

    public ClauseManagerPolishTests(StartupFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PageTitle_UsesInjectedLocalizationService()
    {
        var localization = _fixture.Provider.GetRequiredService<ILocalizationService>();
        await localization.SetLanguageAsync("fr");

        var service = _fixture.Provider.GetRequiredService<IClauseManagementService>();
        var vm = new ClauseManagerViewModel(service, localization);

        Assert.Equal(localization.GetString("Clause.PageTitle"), vm.PageTitle);
    }

    [Fact]
    public async Task ErrorMessages_AreLocalized_UnderNonEnglishCulture()
    {
        var localization = _fixture.Provider.GetRequiredService<ILocalizationService>();
        await localization.SetLanguageAsync("fr");

        var failingService = new FailingClauseService();
        var vm = new ClauseManagerViewModel(failingService, localization);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.DoesNotContain("We couldn't load clauses. Please try again.", vm.ErrorMessage);
        Assert.Equal(localization.GetString("Ui.ClauseErrorLoad"), vm.ErrorMessage);
    }

    [Fact]
    public void ClauseManagerView_HasVisibleFilterLabelsAndColumnHeaders()
    {
        var viewsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "DraftEngine.App", "Views"));
        var xaml = File.ReadAllText(Path.Combine(viewsDir, "ClauseManagerView.xaml"));

        // Must have visible filter labels before the ComboBoxes
        Assert.Contains("{loc:T Ui.ClauseFilterType}", xaml);
        Assert.Contains("{loc:T Ui.ClauseFilterCategory}", xaml);
        Assert.Contains("{loc:T Ui.ClauseFilterSource}", xaml);

        // Must have column headers
        Assert.Contains("{loc:T Ui.ClauseColTitle}", xaml);
        Assert.Contains("{loc:T Ui.ClauseColCategory}", xaml);
        Assert.Contains("{loc:T Ui.ClauseColSource}", xaml);
        Assert.Contains("{loc:T Ui.ClauseColOrder}", xaml);
        Assert.Contains("{loc:T Ui.ClauseColOptional}", xaml);

        // Must not contain raw string format
        Assert.DoesNotContain("StringFormat=' optional'", xaml);
    }

    private sealed class FailingClauseService : IClauseManagementService
    {
        public Task<List<Clause>> GetClausesAsync(string contractType, string? category = null, string? source = null) =>
            throw new InvalidOperationException("Simulated load failure.");

        public Task<List<Clause>> GetUserClausesAsync(string contractType) =>
            throw new NotImplementedException();

        public Task<Clause> CreateUserClauseAsync(string contractType, Clause clause) =>
            throw new NotImplementedException();

        public Task<Clause?> UpdateUserClauseAsync(int clauseId, Clause clause) =>
            throw new NotImplementedException();

        public Task ArchiveUserClauseAsync(int clauseId) =>
            throw new NotImplementedException();

        public Task DuplicateClauseAsync(int sourceClauseId, string targetContractType) =>
            throw new NotImplementedException();

        public Task<(bool IsValid, IEnumerable<string> UnknownKeys)> ValidatePlaceholdersAsync(string bodyTemplate) =>
            Task.FromResult((true, (IEnumerable<string>)new List<string>()));
    }
}
