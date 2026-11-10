using System.Globalization;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.ViewModels;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Sidebar structure and behaviour: exactly one Settings entry pinned at the bottom,
/// Workspace/Tools grouping, an icon for every item, an active marker that follows
/// navigation, and labels that refresh live when the language changes.
/// </summary>
public sealed class SidebarTests : IDisposable
{
    public SidebarTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    [Fact]
    public void Sidebar_HasExactlyOneSettingsEntry_PinnedInBottomGroup()
    {
        var (viewModel, _) = CreateViewModel();

        var settingsItems = viewModel.AllItems.Where(item => item.Key == "settings").ToArray();

        Assert.Single(settingsItems);
        Assert.Contains(settingsItems[0], viewModel.BottomItems);
        Assert.DoesNotContain(viewModel.WorkspaceItems, item => item.Key == "settings");
        Assert.DoesNotContain(viewModel.ToolsItems, item => item.Key == "settings");
    }

    [Fact]
    public void Sidebar_GroupsPagesIntoWorkspaceAndTools()
    {
        var (viewModel, _) = CreateViewModel();

        Assert.Equal(
            new[] { "dashboard", "new-contract", "contracts", "archived-contracts" },
            viewModel.WorkspaceItems.Select(item => item.Key));
        Assert.Equal(
            new[] { "analyze", "clause-management" },
            viewModel.ToolsItems.Select(item => item.Key));
        Assert.Equal(
            new[] { "settings", "about" },
            viewModel.BottomItems.Select(item => item.Key));
    }

    [Fact]
    public void Sidebar_EveryItemHasALabelAndAnIcon()
    {
        var (viewModel, _) = CreateViewModel();

        foreach (var item in viewModel.AllItems)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Label), $"{item.Key} has no label.");
            Assert.False(string.IsNullOrWhiteSpace(item.Icon), $"{item.Key} has no icon glyph.");
        }

        // Icons are private-use glyphs from Segoe Fluent Icons / Segoe MDL2 Assets.
        Assert.All(viewModel.AllItems, item => Assert.True(char.IsSurrogate(char.Parse(item.Icon)) ||
            item.Icon[0] is >= '\uE000' and <= '\uF8FF', $"{item.Key} icon is not a glyph."));
    }

    [Fact]
    public void Sidebar_ActiveMarkerFollowsNavigation()
    {
        var (viewModel, navigation) = CreateViewModel();

        Assert.True(viewModel.WorkspaceItems.Single(item => item.Key == "dashboard").IsActive);

        navigation.Navigate("settings");

        Assert.True(viewModel.BottomItems.Single(item => item.Key == "settings").IsActive);
        Assert.False(viewModel.WorkspaceItems.Single(item => item.Key == "dashboard").IsActive);
        Assert.Equal("settings", viewModel.CurrentPageKey);
    }

    [Fact]
    public async Task Sidebar_LabelsUpdateWhenLanguageChanges()
    {
        var (viewModel, _) = CreateViewModel();
        var service = LocalizationSource.Current.Service!;
        var dashboard = viewModel.WorkspaceItems.Single(item => item.Key == "dashboard");

        Assert.Equal("Dashboard", dashboard.Label);

        var changed = new List<string>();
        dashboard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                changed.Add(e.PropertyName);
            }
        };

        await service.SetLanguageAsync("fr");

        Assert.Equal("Tableau de bord", dashboard.Label);
        Assert.Contains(nameof(NavigationItemViewModel.Label), changed);
        Assert.Equal("Paramètres", viewModel.BottomItems.Single(item => item.Key == "settings").Label);
    }

    [Fact]
    public void Sidebar_OmitsAboutWhenTheFeatureIsDisabled()
    {
        var options = new AppOptions();
        options.Features.AboutEnabled = false;
        var (viewModel, _) = CreateViewModel(options);

        Assert.DoesNotContain(viewModel.BottomItems, item => item.Key == "about");
        Assert.Single(viewModel.BottomItems);
    }

    private static (MainViewModel ViewModel, FakeNavigationService Navigation) CreateViewModel(
        AppOptions? options = null)
    {
        var service = new LocalizationService(new MemoryLanguageStore(), () => CultureInfo.GetCultureInfo("en-US"));
        service.InitializeAsync().GetAwaiter().GetResult();
        LocalizationSource.Initialize(service);

        if (options is null)
        {
            options = new AppOptions();
            options.Features.AboutEnabled = true;
        }

        var navigation = new FakeNavigationService();
        var viewModel = new MainViewModel(navigation, options, updateService: null, localization: service);
        return (viewModel, navigation);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    private sealed class MemoryLanguageStore : ILanguagePreferenceStore
    {
        private string? _value;

        public string? Load() => _value;

        public void Save(string languageCode) => _value = languageCode;
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public event Action<object?>? CurrentViewModelChanged;

        public object? CurrentViewModel { get; private set; }

        public string CurrentKey { get; private set; } = string.Empty;

        public bool Navigate(string key, object? parameter = null)
        {
            CurrentKey = key;
            CurrentViewModel = new object();
            CurrentViewModelChanged?.Invoke(CurrentViewModel);
            return true;
        }
    }
}
