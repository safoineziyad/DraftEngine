using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services.Backup;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Profiles;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.Themes;
using DraftEngine.AI.Services;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// View model for the settings page: OpenAI API key (masked, encrypted at rest), model
/// selection and contract defaults, with connection testing and explicit saving.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _settingsService;
    private readonly IAiRedactionService? _redactionService;
    private readonly IPortableBackupService? _backupService;
    private readonly ILocalizationService _localization;
    private readonly IThemeService? _themeService;
    private readonly IProfileService? _profileService;
    private bool _isApplyingProfile;
    private bool _isLoading;
    private bool _isApplyingLanguage;
    private bool _isApplyingTheme;

    // Status text is stored as a resource key (+ format args) so it can be re-rendered
    // when the user switches language without repeating the action.
    private string? _testStatusKey;
    private object[] _testStatusArgs = Array.Empty<object>();
    private string? _saveStatusKey;
    private object[] _saveStatusArgs = Array.Empty<object>();
    private string? _backupStatusKey;
    private object[] _backupStatusArgs = Array.Empty<object>();

    /// <summary>The page title shown in the view header (recomputed on language change).</summary>
    public string PageTitle => _localization.GetString("Settings.PageTitle");

    /// <summary>Languages offered in the picker, each shown in its own name.</summary>
    public IReadOnlyList<SupportedLanguage> SupportedLanguages => _localization.SupportedLanguages;

    /// <summary>Themes offered in the picker: Follow Windows (default), Light and Dark.</summary>
    public IReadOnlyList<ThemeChoiceViewModel> ThemeOptions { get; }

    /// <summary>Theme selected in the picker; changing it applies and persists immediately.</summary>
    [ObservableProperty]
    private ThemeMode selectedTheme = ThemeMode.System;

    /// <summary>
    /// Model ids offered in the picker: whatever the provider published plus the current manual
    /// id. Nothing is hardcoded and no pricing is attached.
    /// </summary>
    public ObservableCollection<string> ModelOptions { get; } = new();

    /// <summary>Configured providers; the active one is used for drafting.</summary>
    public ObservableCollection<AiProfile> Profiles { get; } = new();

    /// <summary>Preset choices with localized display names.</summary>
    public IReadOnlyList<AiPresetChoice> PresetOptions { get; }

    /// <summary>Profile currently being edited.</summary>
    [ObservableProperty]
    private AiProfile? selectedProfile;

    /// <summary>Display name of the profile being edited.</summary>
    [ObservableProperty]
    private string profileName = string.Empty;

    /// <summary>Transport preset of the profile being edited.</summary>
    [ObservableProperty]
    private AiPresetChoice? selectedPresetOption;

    /// <summary>True while the provider's model list is being fetched.</summary>
    [ObservableProperty]
    private bool isFetchingModels;

    /// <summary>Countries offered as the default governing law, sorted for the active culture.</summary>
    public IReadOnlyList<DraftEngine.Core.Data.CountryInfo> GoverningLawOptions { get; } =
        DraftEngine.Core.Data.CountryCatalog.Sort(
            DraftEngine.Core.Data.CountryCatalog.All,
            System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Subdivisions offered for the selected default country.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> GoverningLawSubdivisions { get; } = new();

    /// <summary>The OpenAI API key being edited (plaintext in memory only; encrypted at rest).</summary>
    [ObservableProperty]
    private string openAiApiKey = string.Empty;

    /// <summary>True when the API key is shown as plain text instead of masked.</summary>
    [ObservableProperty]
    private bool isKeyVisible;

    /// <summary>Language code selected in the picker (e.g. "en", "fr", "ar", "es").</summary>
    [ObservableProperty]
    private string selectedLanguage = "en";

    /// <summary>The selected OpenAI chat model.</summary>
    [ObservableProperty]
    private string selectedModel = AppSettings.FallbackModel;

    /// <summary>The default governing-law country for new contracts.</summary>
    [ObservableProperty]
    private DraftEngine.Core.Data.CountryInfo? selectedGoverningLawCountry;

    /// <summary>The default governing-law subdivision (free text when the country has no list).</summary>
    [ObservableProperty]
    private string selectedGoverningLawSubdivision = string.Empty;

    /// <summary>True when the selected country has a built-in subdivision list.</summary>
    public bool HasGoverningLawSubdivisions => GoverningLawSubdivisions.Count > 0;

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

    /// <summary>True when a custom OpenAI-compatible endpoint (Azure, gateway, Ollama…) is used.</summary>
    [ObservableProperty]
    private bool useCustomOpenAiEndpoint;

    /// <summary>The custom OpenAI-compatible base URL, e.g. http://localhost:11434/v1/.</summary>
    [ObservableProperty]
    private string openAiBaseUrl = string.Empty;

    /// <summary>True when real party names and addresses are replaced before AI requests.</summary>
    [ObservableProperty]
    private bool hidePartyNamesFromAi;

    /// <summary>Signed update flow shared with the banner and About (null in unit tests).</summary>
    public UpdateFlowViewModel? Updates => _updateFlow;

    /// <summary>True when the user has granted AI data-sharing consent.</summary>
    [ObservableProperty]
    private bool aiConsentGranted;

    /// <summary>True when the term-months validation error should be shown.</summary>
    public bool HasTermMonthsError => !string.IsNullOrEmpty(TermMonthsError);

    /// <summary>True when settings can be saved (no blocking validation errors).</summary>
    public bool CanSave => string.IsNullOrEmpty(TermMonthsError);

    /// <summary>API key status text for UI display.</summary>
    [ObservableProperty]
    private string apiKeyStatusText = string.Empty;

    /// <summary>True if an API key is configured.</summary>
    [ObservableProperty]
    private bool hasApiKey;

    /// <summary>Inline validation message for default term months.</summary>
    [ObservableProperty]
    private string? termMonthsError;

    /// <summary>The Show/Hide toggle label for the API key field.</summary>
    public string ShowHideKeyText =>
        _localization.GetString(IsKeyVisible ? "Settings.Hide" : "Settings.Show");

    /// <summary>Localized consent status line shown above the revoke button.</summary>
    public string AiConsentStatusText => _localization.GetString(
        AiConsentGranted ? "Settings.AiConsentGranted" : "Settings.AiConsentNotGranted");

    /// <summary>True when a connection test status message should be shown.</summary>
    public bool HasTestStatus => TestStatusMessage is not null;

    /// <summary>True when a save status message should be shown.</summary>
    public bool HasSaveStatus => SaveStatusMessage is not null;

    /// <summary>Creates the settings view model and loads the saved settings.</summary>
    public SettingsViewModel(
        IAppSettingsService settingsService,
        IAiRedactionService? redactionService = null,
        IPortableBackupService? backupService = null,
        ILocalizationService? localization = null,
        IThemeService? themeService = null,
        IProfileService? profileService = null)
    {
        _profileService = profileService;
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _redactionService = redactionService;
        _backupService = backupService;
        _themeService = themeService;
        _httpClientFactory = httpClientFactory;
        _updateFlow = updateFlow;
        _localization = localization ?? LocalizationSource.Current.Service
            ?? throw new ArgumentNullException(nameof(localization));
        PresetOptions =
        [
            new AiPresetChoice(AiProviderPreset.OpenAiCompatible, "Settings.PresetOpenAi", _localization),
            new AiPresetChoice(AiProviderPreset.AzureOpenAi, "Settings.PresetAzure", _localization),
            new AiPresetChoice(AiProviderPreset.Ollama, "Settings.PresetOllama", _localization),
            new AiPresetChoice(AiProviderPreset.LmStudio, "Settings.PresetLmStudio", _localization),
            new AiPresetChoice(AiProviderPreset.Custom, "Settings.PresetCustom", _localization),
        ];
        ThemeOptions =
        [
            new ThemeChoiceViewModel(ThemeMode.System, "Theme.System", _localization),
            new ThemeChoiceViewModel(ThemeMode.Light, "Theme.Light", _localization),
            new ThemeChoiceViewModel(ThemeMode.Dark, "Theme.Dark", _localization),
        ];
        ProfileOptions =
        [
            new ProfileChoiceViewModel(UserProfile.Lawyer, "Settings.Profile.Lawyer", _localization),
            new ProfileChoiceViewModel(UserProfile.Business, "Settings.Profile.Business", _localization),
            new ProfileChoiceViewModel(UserProfile.Mediator, "Settings.Profile.Mediator", _localization),
        ];
        _isApplyingProfile = true;
        SelectedProfile = profileService?.Current ?? UserProfile.Lawyer;
        _isApplyingProfile = false;

        _settingsService.SettingsChanged += OnSettingsChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        _isApplyingLanguage = true;
        SelectedLanguage = _localization.CurrentLanguage;
        _isApplyingLanguage = false;
        ApplyFromCurrentSettings();
    }

    /// <summary>Profile choices shown in the Settings profile card.</summary>
    public IReadOnlyList<ProfileChoiceViewModel> ProfileOptions { get; }

    /// <summary>The saved profile; changing it re-filters the sidebar immediately.</summary>
    [ObservableProperty]
    private UserProfile selectedProfile = UserProfile.Lawyer;

    /// <summary>Confirmation shown after a profile change.</summary>
    [ObservableProperty]
    private string? profileConfirmation;

    /// <summary>True when the confirmation is visible.</summary>
    public bool HasProfileConfirmation => !string.IsNullOrEmpty(ProfileConfirmation);

    partial void OnProfileConfirmationChanged(string? value) =>
        OnPropertyChanged(nameof(HasProfileConfirmation));

    partial void OnSelectedProfileChanged(UserProfile value)
    {
        if (_isApplyingProfile || _profileService is null || _profileService.Current == value)
        {
            return;
        }

        _ = ApplyProfileAsync(value);
    }

    private async Task ApplyProfileAsync(UserProfile profile)
    {
        await _profileService!.SetProfileAsync(profile);
        var name = ProfileOptions.First(option => option.Profile == profile).DisplayName;
        ProfileConfirmation = _localization.GetString("Settings.ProfileChanged", name);
    }

    /// <summary>Password used for encrypted backup export/restore (never persisted, never logged).</summary>
    [ObservableProperty]
    private string backupPassword = string.Empty;

    /// <summary>True while a backup export/restore is running.</summary>
    [ObservableProperty]
    private bool isBackupBusy;

    /// <summary>Friendly result text for the last backup operation.</summary>
    [ObservableProperty]
    private string? backupStatusMessage;

    /// <summary>True when a backup status message should be shown.</summary>
    public bool HasBackupStatus => !string.IsNullOrEmpty(BackupStatusMessage);

    /// <summary>False while a backup operation is running.</summary>
    public bool CanUseBackup => !IsBackupBusy;

    partial void OnIsBackupBusyChanged(bool value) => OnPropertyChanged(nameof(CanUseBackup));

    /// <summary>Writes a password-encrypted portable backup of all local data.</summary>
    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        if (_backupService is null)
        {
            return;
        }

        if (BackupPassword.Length < 8)
        {
            SetBackupStatus("Settings.ErrorBackupPasswordShort");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Text("Settings.ExportBackupTitle"),
            Filter = Text("Settings.BackupFilter"),
            DefaultExt = ".debk",
            FileName = $"DraftEngine-backup-{DateTime.Now:yyyyMMdd}.debk"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBackupBusy = true;
        try
        {
            await using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
            var summary = await _backupService.ExportEncryptedAsync(stream, BackupPassword, includeApiKey: false);
            SetBackupStatus("Settings.BackupSaved", summary.Contracts, summary.Clauses);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Portable backup export failed.");
            SetBackupStatus("Settings.ErrorBackupWrite");
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    /// <summary>Restores a portable backup after an explicit confirmation.</summary>
    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (_backupService is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Text("Settings.RestoreBackupTitle"),
            Filter = Text("Settings.BackupOpenFilter"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            Text("Settings.RestoreConfirm"),
            Text("Settings.RestoreConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        if (BackupPassword.Length < 8)
        {
            SetBackupStatus("Settings.ErrorRestorePassword");
            return;
        }

        IsBackupBusy = true;
        try
        {
            await using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            var summary = await _backupService.ImportEncryptedAsync(stream, BackupPassword, confirmReplace: true);
            SetBackupStatus("Settings.RestoreDone", summary.Contracts, summary.Clauses);
        }
        catch (BackupPasswordException)
        {
            SetBackupStatus("Settings.ErrorWrongPassword");
        }
        catch (BackupFormatException ex)
        {
            Log.Error(ex, "Portable backup format was not recognized.");
            SetBackupStatus("Settings.ErrorRestore");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Portable backup import failed.");
            SetBackupStatus("Settings.ErrorRestore");
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    /// <summary>Writes a readable JSON export after an unencrypted-file warning.</summary>
    [RelayCommand]
    private async Task ExportDataAsync(string? format)
    {
        if (_backupService is null)
        {
            return;
        }

        var isCsv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
        var warning = Text(isCsv ? "Settings.CsvWarning" : "Settings.JsonWarning");
        if (MessageBox.Show(warning, Text("Settings.ExportDataTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Text("Settings.ExportDataTitle"),
            Filter = Text(isCsv ? "Settings.CsvFilter" : "Settings.JsonFilter"),
            DefaultExt = isCsv ? ".csv" : ".json",
            FileName = $"DraftEngine-data-{DateTime.Now:yyyyMMdd}.{(isCsv ? "csv" : "json")}"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBackupBusy = true;
        try
        {
            await using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
            if (isCsv)
            {
                await _backupService.WriteCsvAsync(stream);
            }
            else
            {
                await _backupService.WriteJsonAsync(stream, includeApiKey: false);
            }

            SetBackupStatus("Settings.ExportDone");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Plain data export failed.");
            SetBackupStatus("Settings.ErrorExport");
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    /// <summary>Masks or reveals the API key.</summary>
    [RelayCommand]
    private void ToggleKeyVisibility() => IsKeyVisible = !IsKeyVisible;

    /// <summary>Withdraws AI data-sharing consent; AI editing is blocked until granted again.</summary>
    [RelayCommand]
    private async Task RevokeAiConsentAsync()
    {
        try
        {
            var current = _settingsService.Current;
            current.AiDataConsentAccepted = false;
            await _settingsService.SaveAsync(current);
            AiConsentGranted = false;
            SetSaveStatus("Settings.ConsentRevoked");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to revoke AI consent.");
            SetSaveStatus("Settings.ErrorConsentRevoke");
        }
    }

    /// <summary>
    /// Tests the API key currently typed in the field (saved or not) against OpenAI and
    /// shows a friendly result. Raw exceptions are never surfaced.
    /// </summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsTestingConnection) return;

        var profile = BuildProfileFromEditor();
        if (string.IsNullOrWhiteSpace(profile.ApiKey) && !profile.AllowsMissingKey)
        {
            IsTestSuccessful = false;
            SetTestStatus("Settings.ErrorApiKeyMissing");
            return;
        }

        IsTestingConnection = true;
        SetTestStatus("Settings.Connecting");
        try
        {
            if (_httpClientFactory is null)
            {
                var aiService = _settingsService.CreateAiService(profile.ApiKey, profile.Model);
                if (aiService is null)
                {
                    IsTestSuccessful = false;
                    SetTestStatus("Settings.ErrorApiKeyMissing");
                    return;
                }

                var legacy = await aiService.TestConnectionDetailedAsync();
                ShowTestResult(legacy.Success, legacy.Message);
                return;
            }

            var client = new AiProviderClient(_httpClientFactory.CreateClient("openai"));
            var result = await client.TestConnectionAsync(profile, profile.Model);
            if (result.Success && result.Models.Count > 0)
            {
                ApplyModelList(result.Models);
            }

            ShowTestResult(result.Success, result.Message);
        }
        catch (Exception ex)
        {
            // Log the detail (never the key); show only a friendly sentence.
            Log.Error(ex, "OpenAI connection test failed.");
            IsTestSuccessful = false;
            SetTestStatus("Settings.ErrorConnectionTest");
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    /// <summary>Adds a new profile and starts editing it.</summary>
    [RelayCommand]
    private void AddProfile()
    {
        var profile = new AiProfile
        {
            Name = Text("Settings.NewProfileName"),
            Preset = AiProviderPreset.OpenAiCompatible,
            BaseUrl = AiProfile.DefaultBaseUrl(AiProviderPreset.OpenAiCompatible),
        };
        Profiles.Add(profile);
        SelectedProfile = profile;
        MarkDirty();
    }

    /// <summary>Removes the profile being edited; at least one profile is always kept.</summary>
    [RelayCommand]
    private void RemoveProfile()
    {
        if (SelectedProfile is null || Profiles.Count <= 1)
        {
            return;
        }

        var index = Profiles.IndexOf(SelectedProfile);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles[Math.Max(0, Math.Min(index, Profiles.Count - 1))];
        MarkDirty();
    }

    /// <summary>Fetches the provider's model list (never invents model ids).</summary>
    [RelayCommand]
    private async Task FetchModelsAsync()
    {
        if (IsFetchingModels)
        {
            return;
        }

        var profile = BuildProfileFromEditor();
        if (string.IsNullOrWhiteSpace(profile.ApiKey) && !profile.AllowsMissingKey)
        {
            SetTestStatus("Settings.ErrorApiKeyMissing");
            return;
        }

        if (_httpClientFactory is null)
        {
            SetTestStatus("Settings.ErrorFetchModels");
            return;
        }

        IsFetchingModels = true;
        try
        {
            var client = new AiProviderClient(_httpClientFactory.CreateClient("openai"));
            var result = await client.TestConnectionAsync(profile, model: null);
            if (!result.Success)
            {
                IsTestSuccessful = false;
                _testStatusKey = null;
                TestStatusMessage = result.Message;
                return;
            }

            ApplyModelList(result.Models);
            IsTestSuccessful = true;
            SetTestStatus("Settings.ModelsFetched", result.Models.Count);
        }
        catch (Exception ex)
        {
            Log.Warning("Model list fetch failed ({ExceptionType}).", ex.GetType().Name);
            IsTestSuccessful = false;
            SetTestStatus("Settings.ErrorFetchModels");
        }
        finally
        {
            IsFetchingModels = false;
        }
    }

    /// <summary>Persists the settings (the API key is encrypted before writing).</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var termMonths = DefaultTermMonths;
        if (termMonths < 1 || termMonths > 120)
        {
            SetSaveStatus("Settings.ErrorTermRange");
            return;
        }

        try
        {
            SetIsLoadingState(true);
            var settings = new AppSettings
            {
                OpenAiApiKey = OpenAiApiKey?.Trim() ?? string.Empty,
                OpenAiModel = SelectedModel,
                DefaultGoverningLaw = DraftEngine.Core.Data.JurisdictionResolver.Compose(
                    SelectedGoverningLawCountry?.Code,
                    SelectedGoverningLawSubdivision,
                    _localization.CurrentLanguage),
                DefaultGoverningLawCountry = SelectedGoverningLawCountry?.Code ?? string.Empty,
                DefaultGoverningLawSubdivision = SelectedGoverningLawSubdivision?.Trim() ?? string.Empty,
                DefaultTermMonths = termMonths,
                AiProfiles = Profiles.Select(profile => profile.Clone()).ToList(),
                ActiveAiProfileId = SelectedProfile?.Id ?? string.Empty,
                // Preserve the AI data-sharing acknowledgement — editing settings must not
                // silently withdraw the consent given during a chat edit.
                AiDataConsentAccepted = AiConsentGranted,
                OnboardingCompleted = _settingsService.Current.OnboardingCompleted,
                OpenAiBaseUrl = string.IsNullOrWhiteSpace(OpenAiBaseUrl) ? null : OpenAiBaseUrl.Trim(),
                UseCustomOpenAiEndpoint = UseCustomOpenAiEndpoint,
                HidePartyNamesFromAi = HidePartyNamesFromAi,
                // Update choices live on the shared update flow; preserve them so Save never resets them.
                UpdateChecksEnabled = _settingsService.Current.UpdateChecksEnabled,
                UpdateIncludeBeta = _settingsService.Current.UpdateIncludeBeta,
                SkippedUpdateVersion = _settingsService.Current.SkippedUpdateVersion,
                LastUpdateCheckUtc = _settingsService.Current.LastUpdateCheckUtc,
                // Persisted elsewhere (theme service / language store); preserve them so a
                // manual Save never resets the user's language or theme choice.
                Theme = _settingsService.Current.Theme,
                Language = _settingsService.Current.Language,
            };
            await _settingsService.SaveAsync(settings);
            if (_redactionService is not null)
            {
                _redactionService.IsEnabled = HidePartyNamesFromAi;
            }
            IsDirty = false;
            SetSaveStatus("Settings.Saved");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings.");
            SetSaveStatus("Settings.ErrorSave");
        }
        finally
        {
            SetIsLoadingState(false);
        }
    }

    partial void OnOpenAiApiKeyChanged(string value)
    {
        MarkDirty();
        UpdateApiKeyStatus(value);
        SyncEditorToProfile();
    }

    partial void OnSelectedModelChanged(string value)
    {
        MarkDirty();
        SyncEditorToProfile();
    }

    partial void OnUseCustomOpenAiEndpointChanged(bool value) => MarkDirty();

    partial void OnOpenAiBaseUrlChanged(string value)
    {
        MarkDirty();
        SyncEditorToProfile();
    }

    partial void OnProfileNameChanged(string value) => SyncEditorToProfile();

    partial void OnSelectedProfileChanged(AiProfile? value) => LoadProfileIntoEditor(value);

    partial void OnSelectedPresetOptionChanged(AiPresetChoice? value)
    {
        if (value is null || _isLoading)
        {
            return;
        }

        // Switching preset replaces a default URL with the new preset's default, but never
        // overwrites a URL the user typed for a custom endpoint.
        var current = OpenAiBaseUrl?.Trim() ?? string.Empty;
        var isAnotherDefault = PresetOptions.Any(option =>
            string.Equals(AiProfile.DefaultBaseUrl(option.Preset), current, StringComparison.OrdinalIgnoreCase));
        var nextDefault = AiProfile.DefaultBaseUrl(value.Preset);
        if (current.Length == 0 || isAnotherDefault)
        {
            OpenAiBaseUrl = nextDefault;
        }

        MarkDirty();
        SyncEditorToProfile();
    }

    /// <summary>Copies the editor fields into the selected profile (no-op while loading).</summary>
    private void SyncEditorToProfile()
    {
        if (_isLoading || SelectedProfile is null)
        {
            return;
        }

        SelectedProfile.Name = string.IsNullOrWhiteSpace(ProfileName) ? Text("Settings.NewProfileName") : ProfileName.Trim();
        SelectedProfile.Preset = SelectedPresetOption?.Preset ?? SelectedProfile.Preset;
        SelectedProfile.BaseUrl = OpenAiBaseUrl?.Trim() ?? string.Empty;
        SelectedProfile.Model = SelectedModel?.Trim() ?? string.Empty;
        SelectedProfile.ApiKey = OpenAiApiKey?.Trim() ?? string.Empty;
        MarkDirty();
    }

    /// <summary>Shows a profile in the editor (also used when the selection changes).</summary>
    private void LoadProfileIntoEditor(AiProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            ProfileName = profile.Name;
            SelectedPresetOption = PresetOptions.FirstOrDefault(option => option.Preset == profile.Preset) ?? PresetOptions[0];
            OpenAiApiKey = profile.ApiKey;
            OpenAiBaseUrl = profile.BaseUrl;
            SelectedModel = profile.Model;
            EnsureModelOption(profile.Model);
        }
        finally
        {
            _isLoading = wasLoading;
        }
    }

    /// <summary>Builds a throwaway profile from the editor, for testing before saving.</summary>
    private AiProfile BuildProfileFromEditor() => new()
    {
        Id = SelectedProfile?.Id ?? "editor",
        Name = ProfileName,
        Preset = SelectedPresetOption?.Preset ?? AiProviderPreset.OpenAiCompatible,
        BaseUrl = OpenAiBaseUrl?.Trim() ?? string.Empty,
        Model = SelectedModel?.Trim() ?? string.Empty,
        ApiKey = OpenAiApiKey?.Trim() ?? string.Empty,
    };

    /// <summary>Replaces the model picker with the provider's list, keeping the current id.</summary>
    private void ApplyModelList(IReadOnlyList<string> models)
    {
        var current = SelectedModel?.Trim() ?? string.Empty;
        ModelOptions.Clear();
        foreach (var model in models)
        {
            ModelOptions.Add(model);
        }

        if (current.Length > 0)
        {
            EnsureModelOption(current);
            SelectedModel = current;
        }
        else if (ModelOptions.Count > 0)
        {
            SelectedModel = ModelOptions[0];
        }
    }

    private void EnsureModelOption(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return;
        }

        if (!ModelOptions.Contains(model))
        {
            ModelOptions.Add(model);
        }
    }

    private void ShowTestResult(bool success, string? message)
    {
        IsTestSuccessful = success;
        if (success)
        {
            SetTestStatus("Settings.Connected");
        }
        else if (string.IsNullOrWhiteSpace(message))
        {
            SetTestStatus("Settings.ErrorConnect");
        }
        else
        {
            // Provider message already carries a user-safe sentence (not translated content).
            _testStatusKey = null;
            TestStatusMessage = message;
        }
    }

    partial void OnHidePartyNamesFromAiChanged(bool value) => MarkDirty();

    partial void OnSelectedGoverningLawCountryChanged(DraftEngine.Core.Data.CountryInfo? value)
    {
        RefreshGoverningLawSubdivisions();
        MarkDirty();
    }

    partial void OnSelectedGoverningLawSubdivisionChanged(string value) => MarkDirty();

    private void RefreshGoverningLawSubdivisions()
    {
        GoverningLawSubdivisions.Clear();
        foreach (var subdivision in DraftEngine.Core.Data.SubdivisionCatalog.For(SelectedGoverningLawCountry?.Code))
        {
            GoverningLawSubdivisions.Add(subdivision);
        }

        OnPropertyChanged(nameof(HasGoverningLawSubdivisions));
    }

    partial void OnDefaultTermMonthsChanged(int value)
    {
        ValidateTermMonths(value);
        MarkDirty();
    }

    partial void OnIsKeyVisibleChanged(bool value) => OnPropertyChanged(nameof(ShowHideKeyText));

    partial void OnAiConsentGrantedChanged(bool value) =>
        OnPropertyChanged(nameof(AiConsentStatusText));

    partial void OnSelectedThemeChanged(ThemeMode value)
    {
        if (_isApplyingTheme || _themeService is null)
        {
            return;
        }

        // Applies the theme live and persists the choice through the settings service.
        _ = _themeService.SetModeAsync(value);
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (_isApplyingLanguage || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // Fire-and-forget is safe: SetLanguageAsync persists and raises LanguageChanged,
        // which refreshes PageTitle and every live {loc:T} binding.
        _ = _localization.SetLanguageAsync(value);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(SupportedLanguages));
        OnPropertyChanged(nameof(ThemeOptions));
        foreach (var option in ThemeOptions)
        {
            option.RefreshLocalization();
        }

        foreach (var option in ProfileOptions)
        {
            option.RefreshLocalization();
        }

        OnPropertyChanged(nameof(ProfileConfirmation));
        OnPropertyChanged(nameof(AiConsentStatusText));
        OnPropertyChanged(nameof(ShowHideKeyText));
        OnPropertyChanged(nameof(PresetOptions));
        foreach (var option in PresetOptions)
        {
            option.RefreshLocalization();
        }

        // Re-render values composed from resource keys in the new language.
        UpdateApiKeyStatus(OpenAiApiKey);
        ValidateTermMonths(DefaultTermMonths);

        if (_testStatusKey is not null)
        {
            TestStatusMessage = Text(_testStatusKey, _testStatusArgs);
        }

        if (_saveStatusKey is not null)
        {
            SaveStatusMessage = Text(_saveStatusKey, _saveStatusArgs);
        }

        if (_backupStatusKey is not null)
        {
            BackupStatusMessage = Text(_backupStatusKey, _backupStatusArgs);
        }

        if (!string.Equals(SelectedLanguage, _localization.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _isApplyingLanguage = true;
            SelectedLanguage = _localization.CurrentLanguage;
            _isApplyingLanguage = false;
        }
    }

    partial void OnTermMonthsErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasTermMonthsError));
    }

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
            Profiles.Clear();
            foreach (var profile in saved.AiProfiles)
            {
                Profiles.Add(profile.Clone());
            }

            SelectedProfile = Profiles.FirstOrDefault(profile =>
                    string.Equals(profile.Id, saved.ActiveAiProfileId, StringComparison.Ordinal))
                ?? Profiles.FirstOrDefault();

            OpenAiApiKey = saved.OpenAiApiKey;
            UpdateApiKeyStatus(saved.OpenAiApiKey);
            SelectedModel = saved.OpenAiModel;
            EnsureModelOption(saved.OpenAiModel);
            SelectedGoverningLawCountry = DraftEngine.Core.Data.CountryCatalog.Find(saved.DefaultGoverningLawCountry)
                ?? DraftEngine.Core.Data.CountryCatalog.Find(DraftEngine.Core.Data.CountryCatalog.DefaultCode());
            SelectedGoverningLawSubdivision = saved.DefaultGoverningLawSubdivision;
            RefreshGoverningLawSubdivisions();
            _isApplyingTheme = true;
            SelectedTheme = ThemeResolver.Parse(saved.Theme);
            _isApplyingTheme = false;
            DefaultTermMonths = saved.DefaultTermMonths;
            ValidateTermMonths(saved.DefaultTermMonths);
            UseCustomOpenAiEndpoint = saved.UseCustomOpenAiEndpoint;
            OpenAiBaseUrl = saved.OpenAiBaseUrl ?? string.Empty;
            HidePartyNamesFromAi = saved.HidePartyNamesFromAi;
            AiConsentGranted = saved.AiDataConsentAccepted;
            IsDirty = false;
        }
        finally
        {
            SetIsLoadingState(false);
        }
    }

    private void UpdateApiKeyStatus(string? key)
    {
        var trimmed = key?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            HasApiKey = false;
            ApiKeyStatusText = Text("Settings.ApiKeyNotConfigured");
            return;
        }

        HasApiKey = true;
        ApiKeyStatusText = trimmed.Length > 8
            ? Text("Settings.ApiKeyConfiguredMasked", trimmed.Substring(0, 3), trimmed.Substring(trimmed.Length - 4))
            : Text("Settings.ApiKeyConfigured");
    }

    private void ValidateTermMonths(int value)
    {
        TermMonthsError = value < 1 || value > 120 ? Text("Settings.ErrorTermRange") : null;
    }

    private string Text(string key) => _localization.GetString(key);

    private string Text(string key, params object[] args) => _localization.GetString(key, args);

    private void SetTestStatus(string key, params object[] args)
    {
        _testStatusKey = key;
        _testStatusArgs = args;
        TestStatusMessage = Text(key, args);
    }

    private void SetSaveStatus(string key, params object[] args)
    {
        _saveStatusKey = key;
        _saveStatusArgs = args;
        SaveStatusMessage = Text(key, args);
    }

    private void SetBackupStatus(string key, params object[] args)
    {
        _backupStatusKey = key;
        _backupStatusArgs = args;
        BackupStatusMessage = Text(key, args);
    }
}
