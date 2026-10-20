using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Interfaces;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// View model for the settings page: OpenAI API key (masked, encrypted at rest), model
/// selection and contract defaults, with connection testing and explicit saving.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _settingsService;
    private bool _isLoading;

    /// <summary>The page title shown in the view header.</summary>
    public string PageTitle => "Settings";

    /// <summary>OpenAI chat model choices.</summary>
    public IReadOnlyList<string> ModelOptions { get; } = new[] { "gpt-4o-mini", "gpt-4o" };

    /// <summary>Default governing law choices for new contracts.</summary>
    public IReadOnlyList<string> GoverningLawOptions { get; } = new[] { "California", "New York", "Delaware", "Texas" };

    /// <summary>The OpenAI API key being edited (plaintext in memory only; encrypted at rest).</summary>
    [ObservableProperty]
    private string openAiApiKey = string.Empty;

    /// <summary>True when the API key is shown as plain text instead of masked.</summary>
    [ObservableProperty]
    private bool isKeyVisible;

    /// <summary>The selected OpenAI chat model.</summary>
    [ObservableProperty]
    private string selectedModel = AppSettings.FallbackModel;

    /// <summary>The default governing law for new contracts.</summary>
    [ObservableProperty]
    private string selectedGoverningLaw = "California";

    /// <summary>The default contract term in months.</summary>
    [ObservableProperty]
    private int defaultTermMonths = 12;

    /// <summary>True while a connection test is running.</summary>
    [ObservableProperty]
    private bool isTestingConnection;

    /// <summary>True when the last connection test succeeded (controls the status color).</summary>
    [ObservableProperty]
    private bool isTestSuccessful;

    /// <summary>Friendly status text for the last connection test; null when there is none.</summary>
    [ObservableProperty]
    private string? testStatusMessage;

    /// <summary>Friendly status text after saving; null when there is none.</summary>
    [ObservableProperty]
    private string? saveStatusMessage;

    /// <summary>True when there are unsaved changes.</summary>
    [ObservableProperty]
    private bool isDirty;

    /// <summary>The Show/Hide toggle label for the API key field.</summary>
    public string ShowHideKeyText => IsKeyVisible ? "Hide" : "Show";

    /// <summary>True when a connection test status message should be shown.</summary>
    public bool HasTestStatus => TestStatusMessage is not null;

    /// <summary>True when a save status message should be shown.</summary>
    public bool HasSaveStatus => SaveStatusMessage is not null;

    /// <summary>Creates the settings view model and loads the saved settings.</summary>
    public SettingsViewModel(IAppSettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _settingsService.SettingsChanged += OnSettingsChanged;
        ApplyFromCurrentSettings();
    }

    /// <summary>Masks or reveals the API key.</summary>
    [RelayCommand]
    private void ToggleKeyVisibility() => IsKeyVisible = !IsKeyVisible;

    /// <summary>
    /// Tests the API key currently typed in the field (saved or not) against OpenAI and
    /// shows a friendly result. Raw exceptions are never surfaced.
    /// </summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsTestingConnection) return;

        var key = OpenAiApiKey?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            IsTestSuccessful = false;
            TestStatusMessage = "Please paste your OpenAI API key first.";
            return;
        }

        IsTestingConnection = true;
        TestStatusMessage = "Connecting…";
        try
        {
            var aiService = _settingsService.CreateAiService(key, SelectedModel);
            if (aiService is null)
            {
                IsTestSuccessful = false;
                TestStatusMessage = "Please paste your OpenAI API key first.";
                return;
            }

            var result = await aiService.TestConnectionDetailedAsync();
            IsTestSuccessful = result.Success;
            TestStatusMessage = result.Success
                ? "Connected to OpenAI successfully."
                : string.IsNullOrWhiteSpace(result.Message)
                    ? "Could not connect to OpenAI. Please try again."
                    : result.Message;
        }
        catch (Exception ex)
        {
            // Log the detail (never the key); show only a friendly sentence.
            Log.Error(ex, "OpenAI connection test failed.");
            IsTestSuccessful = false;
            TestStatusMessage = "Something went wrong while testing the connection. Please try again.";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    /// <summary>Persists the settings (the API key is encrypted before writing).</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var termMonths = DefaultTermMonths;
        if (termMonths < 1 || termMonths > 120)
        {
            SaveStatusMessage = "Term must be between 1 and 120 months.";
            return;
        }

        try
        {
            SetIsLoadingState(true);
            var settings = new AppSettings
            {
                OpenAiApiKey = OpenAiApiKey?.Trim() ?? string.Empty,
                OpenAiModel = SelectedModel,
                DefaultGoverningLaw = SelectedGoverningLaw,
                DefaultTermMonths = termMonths,
            };
            await _settingsService.SaveAsync(settings);
            IsDirty = false;
            SaveStatusMessage = "Settings saved.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings.");
            SaveStatusMessage = "We couldn't save your settings. Please try again.";
        }
        finally
        {
            SetIsLoadingState(false);
        }
    }

    partial void OnOpenAiApiKeyChanged(string value) => MarkDirty();

    partial void OnSelectedModelChanged(string value) => MarkDirty();

    partial void OnSelectedGoverningLawChanged(string value) => MarkDirty();

    partial void OnDefaultTermMonthsChanged(int value) => MarkDirty();

    partial void OnIsKeyVisibleChanged(bool value) => OnPropertyChanged(nameof(ShowHideKeyText));

    partial void OnTestStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasTestStatus));

    partial void OnSaveStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasSaveStatus));

    private void MarkDirty()
    {
        if (!_isLoading)
            IsDirty = true;
    }

    private void SetIsLoadingState(bool isLoading)
    {
        _isLoading = isLoading;
    }

    private void OnSettingsChanged() => ApplyFromCurrentSettings();

    private void ApplyFromCurrentSettings()
    {
        var saved = _settingsService.Current;

        SetIsLoadingState(true);
        try
        {
            OpenAiApiKey = saved.OpenAiApiKey;
            SelectedModel = ModelOptions.Contains(saved.OpenAiModel) ? saved.OpenAiModel : AppSettings.FallbackModel;
            SelectedGoverningLaw = saved.DefaultGoverningLaw;
            DefaultTermMonths = saved.DefaultTermMonths;
            IsDirty = false;
        }
        finally
        {
            SetIsLoadingState(false);
        }
    }
}
