using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.Services.Updates;
using DraftEngine.Updates;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// Drives the signed update flow in the UI: the banner state, release notes, manual checks,
/// progress and the confirm-before-install step. Nothing is downloaded until the user clicks
/// Update, and nothing is installed without the confirmation dialog.
/// </summary>
public partial class UpdateFlowViewModel : ObservableObject
{
    private readonly UpdateService _service;
    private readonly DraftEngine.Updates.UpdateOptions _flowOptions;
    private readonly IAppSettingsService _settings;
    private readonly IUpdatePrompt _prompt;
    private readonly UpdateSnapshotService _snapshot;
    private readonly ILocalizationService _localization;
    private UpdateManifest? _manifest;
    private bool _remindedLater;

    /// <summary>Raised after a confirmed install so the shell can shut down cleanly.</summary>
    public event Action? ShutdownRequested;

    /// <summary>True when an update banner should be visible.</summary>
    [ObservableProperty]
    private bool isUpdateAvailable;

    /// <summary>True while the release notes panel is expanded.</summary>
    [ObservableProperty]
    private bool showReleaseNotes;

    /// <summary>True while downloading or launching.</summary>
    [ObservableProperty]
    private bool isBusy;

    /// <summary>Download progress 0..1.</summary>
    [ObservableProperty]
    private double downloadProgress;

    /// <summary>Localized status line (checking, up to date, last checked).</summary>
    [ObservableProperty]
    private string? statusMessage;

    /// <summary>Localized inline error for the banner/Settings section.</summary>
    [ObservableProperty]
    private string? errorMessage;

    /// <summary>True when the user enabled automatic checks in Settings.</summary>
    [ObservableProperty]
    private bool automaticChecksEnabled;

    /// <summary>True when the beta channel is used.</summary>
    [ObservableProperty]
    private bool includeBeta;

    /// <summary>Creates the flow view model and wires it to the settings and prompts.</summary>
    public UpdateFlowViewModel(
        IAppSettingsService settings,
        AppOptions options,
        IUpdatePrompt prompt,
        UpdateSnapshotService snapshot,
        ILocalizationService localization,
        IHttpClientFactory httpClientFactory,
        IUpdateClock? clock = null,
        IInstallerLauncher? launcher = null,
        IAuthenticodeVerifier? authenticode = null)
        : this(
            settings,
            BuildDependencies(settings, options, httpClientFactory, clock, launcher, authenticode),
            prompt,
            snapshot,
            localization)
    {
    }

    /// <summary>Wires an already constructed service (tests use a fake handler and launcher).</summary>
    public UpdateFlowViewModel(
        IAppSettingsService settings,
        DraftEngine.Updates.UpdateOptions flowOptions,
        UpdateService service,
        IUpdatePrompt prompt,
        UpdateSnapshotService snapshot,
        ILocalizationService localization)
        : this(settings, (flowOptions, service), prompt, snapshot, localization)
    {
    }

    private UpdateFlowViewModel(
        IAppSettingsService settings,
        (DraftEngine.Updates.UpdateOptions Options, UpdateService Service) dependencies,
        IUpdatePrompt prompt,
        UpdateSnapshotService snapshot,
        ILocalizationService localization)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _flowOptions = dependencies.Options;
        _service = dependencies.Service;

        _automaticChecksEntry = settings.Current.UpdateChecksEnabled;
        _includeBetaEntry = settings.Current.UpdateIncludeBeta;
        AutomaticChecksEnabled = _automaticChecksEntry;
        IncludeBeta = _includeBetaEntry;
        _localization.LanguageChanged += (_, _) =>
        {
            RaiseAllTextProperties();
        };
    }

    private static (DraftEngine.Updates.UpdateOptions Options, UpdateService Service) BuildDependencies(
        IAppSettingsService settings,
        AppOptions? options,
        IHttpClientFactory httpClientFactory,
        IUpdateClock? clock,
        IInstallerLauncher? launcher,
        IAuthenticodeVerifier? authenticode)
    {
        var config = options?.Updates ?? new DraftEngine.App.Configuration.UpdateOptions();
        var flowOptions = new DraftEngine.Updates.UpdateOptions
        {
            ChecksEnabled = (options?.Features.AutoUpdateEnabled ?? true) && settings.Current.UpdateChecksEnabled,
            ManifestUrl = config.ManifestUrl,
            BetaManifestUrl = config.BetaManifestUrl,
            AllowedInstallerHosts = config.AllowedInstallerHosts,
            PublicKeySpkiBase64 = config.PublicKey,
            RequireAuthenticode = config.RequireAuthenticode,
            UserAgent = "DraftEngine/" + AppVersion.Display,
        };

        IManifestVerifier verifier = string.IsNullOrWhiteSpace(config.PublicKey)
            ? new RejectAllManifestVerifier()
            : new EcdsaManifestVerifier(config.PublicKey);

        var service = new UpdateService(
            httpClientFactory.CreateClient("updates"),
            flowOptions,
            new SettingsUpdateStateStore(settings),
            clock ?? new SystemUpdateClock(),
            verifier,
            SemanticVersion.TryParse(AppVersion.Display, out var current)
                ? current
                : SemanticVersion.Parse("0.0.0"),
            launcher,
            authenticode);

        return (flowOptions, service);
    }

    private bool _automaticChecksEntry;
    private bool _includeBetaEntry;

    /// <summary>The running informational version (without build metadata).</summary>
    public string CurrentVersion => AppVersion.Display;

    /// <summary>True when the banner is visible (available, not postponed for this session).</summary>
    public bool IsBannerVisible => IsUpdateAvailable && !_remindedLater;

    /// <summary>True when the update must be installed before using the app again.</summary>
    public bool IsRequired => _manifest?.IsRequiredFor(
        SemanticVersion.TryParse(CurrentVersion, out var current) ? current : SemanticVersion.Parse("0.0.0")) ?? false;

    /// <summary>Localized "Version X is available" line.</summary>
    public string BannerText => _manifest is null
        ? string.Empty
        : _localization.GetString("Update.Banner", _manifest.Version);

    /// <summary>Release notes for the current language, falling back to English.</summary>
    public string ReleaseNotes => _manifest?.NotesFor(_localization.CurrentLanguage) ?? string.Empty;

    /// <summary>True when there are notes worth showing.</summary>
    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);

    /// <summary>True when the notes panel is expanded and has content.</summary>
    public bool ShowNotesPanel => ShowReleaseNotes && HasReleaseNotes;

    /// <summary>Localized label of the Update button.</summary>
    public string UpdateButtonText => IsBusy
        ? _localization.GetString("Update.Downloading")
        : _localization.GetString("Update.Install");

    /// <summary>True when the Update button can run.</summary>
    public bool CanDownload => IsBannerVisible && !IsBusy;

    /// <summary>Localized "last checked" line for About/Settings.</summary>
    public string LastCheckedText
    {
        get
        {
            var last = _settings.Current.LastUpdateCheckUtc;
            return last is null
                ? _localization.GetString("Update.LastCheckedNever")
                : _localization.GetString(
                    "Update.LastCheckedAt",
                    last.Value.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture));
        }
    }

    /// <summary>Localized install-confirmation sentence.</summary>
    internal string ConfirmInstallMessage => _localization.GetString(
        "Update.ConfirmBody", _manifest?.Version ?? string.Empty);

    /// <summary>Localized confirmation title.</summary>
    internal string ConfirmInstallTitle => _localization.GetString("Update.ConfirmTitle");

    /// <summary>Error dialog title.</summary>
    internal string ErrorTitle => _localization.GetString("Update.ErrorTitle");

    partial void OnIsUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBannerVisible));
        OnPropertyChanged(nameof(CanDownload));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(UpdateButtonText));
    }

    partial void OnShowReleaseNotesChanged(bool value) => OnPropertyChanged(nameof(ShowNotesPanel));

    partial void OnAutomaticChecksEnabledChanged(bool value)
    {
        if (value == _automaticChecksEntry)
        {
            return;
        }

        _automaticChecksEntry = value;
        _flowOptions.ChecksEnabled = value;
        PersistSettings(settings => settings.UpdateChecksEnabled = value);
        StatusMessage = _localization.GetString(value ? "Update.ChecksOn" : "Update.ChecksOff");
    }

    partial void OnIncludeBetaChanged(bool value)
    {
        if (value == _includeBetaEntry)
        {
            return;
        }

        _includeBetaEntry = value;
        PersistSettings(settings => settings.UpdateIncludeBeta = value);
    }

    /// <summary>Startup hook: waits 15 seconds, then runs one automatic (throttled) check.</summary>
    public async Task CheckOnStartupAsync(TimeSpan? delay = null)
    {
        if (!AutomaticChecksEnabled)
        {
            return;
        }

        var wait = delay ?? _flowOptions.StartupDelay;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait).ConfigureAwait(true);
        }

        await CheckAsync(manual: false).ConfigureAwait(true);
    }

    /// <summary>Checks now (manual checks ignore the 24-hour cadence).</summary>
    [RelayCommand]
    public async Task CheckAsync(bool manual = true)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = _localization.GetString("Update.Checking");
        try
        {
            var channel = IncludeBeta ? UpdateChannel.Beta : UpdateChannel.Stable;
            var result = await _service.CheckAsync(channel, manual).ConfigureAwait(true);
            OnPropertyChanged(nameof(LastCheckedText));

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    _manifest = result.Manifest;
                    _remindedLater = false;
                    IsUpdateAvailable = true;
                    StatusMessage = BannerText;
                    OnPropertyChanged(nameof(BannerText));
                    OnPropertyChanged(nameof(ReleaseNotes));
                    OnPropertyChanged(nameof(HasReleaseNotes));
                    OnPropertyChanged(nameof(IsRequired));
                    break;
                case UpdateCheckStatus.Skipped:
                    _manifest = result.Manifest;
                    IsUpdateAvailable = false;
                    StatusMessage = _localization.GetString("Update.Skipped");
                    break;
                case UpdateCheckStatus.UpToDate:
                    _manifest = null;
                    IsUpdateAvailable = false;
                    StatusMessage = _localization.GetString("Update.UpToDate");
                    break;
                case UpdateCheckStatus.Throttled:
                    StatusMessage = null;
                    break;
                case UpdateCheckStatus.Disabled:
                    IsUpdateAvailable = false;
                    StatusMessage = _localization.GetString("Update.Error.Disabled");
                    break;
                default:
                    IsUpdateAvailable = false;
                    ShowError(result.Error?.ResourceKey ?? "Update.Error.Generic", result.Error?.Code);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Update check failed ({ExceptionType}).", ex.GetType().Name);
            ShowError("Update.Error.Generic", UpdateErrorCode.Generic);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Downloads the verified installer, asks for explicit confirmation, then snapshots and
    /// launches it. Declining the confirmation leaves the app running and does nothing else.
    /// </summary>
    [RelayCommand]
    public async Task DownloadAndInstallAsync()
    {
        if (_manifest is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        DownloadProgress = 0;
        StatusMessage = _localization.GetString("Update.Downloading");
        try
        {
            var progress = new Progress<double>(value => DownloadProgress = value);
            var download = await _service.DownloadAsync(_manifest, progress).ConfigureAwait(true);
            if (!download.Success || download.FilePath is null)
            {
                ShowError(download.Error?.ResourceKey ?? "Update.Error.Generic", download.Error?.Code);
                return;
            }

            if (!_prompt.ConfirmInstall(ConfirmInstallMessage, ConfirmInstallTitle))
            {
                StatusMessage = _localization.GetString("Update.Cancelled");
                return;
            }

            var install = await _service.InstallAsync(
                download.FilePath,
                () => _snapshot.CreatePreUpdateSnapshotAsync(_manifest.Version)).ConfigureAwait(true);
            if (!install.Success)
            {
                ShowError(install.Error?.ResourceKey ?? "Update.Error.Generic", install.Error?.Code);
                return;
            }

            StatusMessage = _localization.GetString("Update.Installing");
            ShutdownRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warning("Update download/install failed ({ExceptionType}).", ex.GetType().Name);
            ShowError("Update.Error.Generic", UpdateErrorCode.Generic);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Hides the banner for this session; About/Settings can still check.</summary>
    [RelayCommand]
    private void RemindLater()
    {
        _remindedLater = true;
        OnPropertyChanged(nameof(IsBannerVisible));
    }

    /// <summary>Remembers the exact version so it is not offered again.</summary>
    [RelayCommand]
    private void SkipVersion()
    {
        if (_manifest is not null)
        {
            _service.SkipVersion(_manifest);
            StatusMessage = _localization.GetString("Update.Skipped");
        }

        IsUpdateAvailable = false;
    }

    /// <summary>Expands or collapses the release notes panel.</summary>
    [RelayCommand]
    private void ToggleReleaseNotes() => ShowReleaseNotes = !ShowReleaseNotes;

    private void ShowError(string resourceKey, UpdateErrorCode? code)
    {
        var message = _localization.GetString(resourceKey);
        ErrorMessage = message;
        StatusMessage = null;
        _prompt.ShowError(message, ErrorTitle);
        Log.Warning("Update flow error: {Code}.", code?.ToString() ?? resourceKey);
    }

    private void PersistSettings(Action<AppSettings> update)
    {
        try
        {
            var current = _settings.Current;
            update(current);
            _ = _settings.SaveAsync(current);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not save update settings ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private void RaiseAllTextProperties()
    {
        OnPropertyChanged(nameof(CurrentVersion));
        OnPropertyChanged(nameof(BannerText));
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(HasReleaseNotes));
        OnPropertyChanged(nameof(LastCheckedText));
    }
}
