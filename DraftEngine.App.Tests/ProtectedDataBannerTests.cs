using System.Globalization;
using System.Security.Cryptography;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.ViewModels;
using DraftEngine.Data;

namespace DraftEngine.App.Tests;

/// <summary>The shell surfaces undecryptable DPAPI data as a localized, content-free banner.</summary>
public sealed class ProtectedDataBannerTests
{
    [Fact]
    public async Task ReportingUnavailableData_ShowsLocalizedWarning_ThatCanBeDismissed()
    {
        var localization = new LocalizationService(
            new MemoryLanguageStore(), () => CultureInfo.GetCultureInfo("en-US"));
        await localization.InitializeAsync();
        LocalizationSource.Initialize(localization);

        var viewModel = new MainViewModel(
            new FakeNavigationService(),
            new AppOptions(),
            localization: localization,
            sensitiveData: new SensitiveDataInterceptor());

        Assert.False(viewModel.HasProtectedDataWarning);

        viewModel.ReportProtectedDataUnavailable(new ProtectedDataUnavailableException(
            "Contract", "AssembledText", new CryptographicException("different user profile")));

        Assert.True(viewModel.HasProtectedDataWarning);
        Assert.Equal(localization.GetString("Audit.ProtectedDataUnavailable"), viewModel.ProtectedDataWarning);
        Assert.DoesNotContain("AssembledText", viewModel.ProtectedDataWarning!, StringComparison.Ordinal);

        viewModel.DismissProtectedDataWarningCommand.Execute(null);

        Assert.False(viewModel.HasProtectedDataWarning);
        Assert.Null(viewModel.ProtectedDataWarning);
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
