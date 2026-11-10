using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.Themes;
using DraftEngine.App.ViewModels;
using DraftEngine.App.Views;
using DraftEngine.Core.Interfaces;
using DraftEngine.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace DraftEngine.App;

/// <summary>
/// Application entry point: builds the generic host, wires up logging and global
/// error handling, and shows the main window.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private AppOptions? _appOptions;

    /// <summary>
    /// Loads configuration (appsettings.json + environment), configures file logging,
    /// builds the host, ensures the database exists and shows the main window.
    /// Any startup failure is logged and shown to the user instead of crashing silently.
    /// A missing OpenAI API key never prevents startup (see ServiceRegistration).
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var upgradeVerification = e.Args.Contains("--verify-upgrade", StringComparer.Ordinal);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            // Configuration: appsettings.json lives next to the executable (copied at build),
            // environment variables are loaded on top, then AppOptions.Load applies the
            // OPENAI_API_KEY override so a real key never needs to live in the file.
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile(
                    $"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json",
                    optional: true,
                    reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            _appOptions = AppOptions.Load(configuration);
            if (upgradeVerification &&
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DRAFTENGINE_DATA_DIR")))
            {
                _appOptions.Serilog.FilePath = Path.Combine(
                    Environment.GetEnvironmentVariable("DRAFTENGINE_DATA_DIR")!,
                    "upgrade-smoke-.log");
            }

            // Rolling file log from the Serilog section (environment variables expanded).
            var logFilePath = Environment.ExpandEnvironmentVariables(_appOptions.Serilog.FilePath);
            var logDirectory = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14)
                .CreateLogger();

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services => services.AddDraftEngine(_appOptions))
                .Build();

            // Applies migrations and creates %APPDATA%\DraftEngine; throws a friendly
            // InvalidOperationException which is surfaced below instead of an opaque crash.
            DatabaseService.EnsureDatabaseCreated(_host.Services);

            if (upgradeVerification)
            {
                VerifyUpgradeDatabase();
                Shutdown(0);
                return;
            }

            // Wire up localization before any window is created: load the saved/Windows
            // language, expose a bindable source for {loc:T Key}, and set the WPF text
            // language so shaping and digit substitution use the right culture.
            var locService = _host.Services.GetRequiredService<ILocalizationService>();
            locService.InitializeAsync().GetAwaiter().GetResult();
            LocalizationSource.Initialize(locService);
            Application.Current.Resources["LocalizationService"] = locService;
            ApplyWpfLanguage(locService.Culture);

            // Keep the in-memory redaction switch aligned with the saved privacy setting
            // from the very first AI request.
            var settingsService = _host.Services.GetRequiredService<IAppSettingsService>();
            var redactionService = _host.Services.GetService<IAiRedactionService>();
            if (redactionService is not null)
            {
                redactionService.IsEnabled = settingsService.Current.HidePartyNamesFromAi;
            }

            // Apply the saved Light/Dark/Follow-Windows theme before the first window is
            // created so there is no light flash and every brush resolves at load time.
            var themeService = _host.Services.GetRequiredService<IThemeService>();
            themeService.Initialize(settingsService.Current.Theme);

            // Offline license gate (only when Features:LicensingEnabled is on). With the
            // default configuration this block is skipped entirely and startup is unchanged.
            if (_appOptions.Features.LicensingEnabled)
            {
                var licenseService = _host.Services.GetRequiredService<
                    DraftEngine.App.Services.Licensing.ILicenseService>();
                var evaluation = licenseService.LoadAsync().GetAwaiter().GetResult();
                if (!evaluation.IsUsable)
                {
                    var activation = new LicenseActivationWindow(licenseService);
                    ThemeService.TrySetDarkTitleBar(activation, themeService.AppliedTheme == AppliedTheme.Dark);
                    if (activation.ShowDialog() != true)
                    {
                        Shutdown();
                        return;
                    }
                }
            }

            var mainWindow = new MainWindow
            {
                DataContext = _host.Services.GetRequiredService<MainViewModel>()
            };
            ThemeService.TrySetDarkTitleBar(mainWindow, themeService.AppliedTheme == AppliedTheme.Dark);
            MainWindow = mainWindow;
            mainWindow.Show();

            // First-run onboarding is opt-in (Features:OnboardingEnabled). Dismissing the
            // dialog counts as skipping, so it is only ever shown once.
            if (_appOptions.Features.OnboardingEnabled && !settingsService.Current.OnboardingCompleted)
            {
                try
                {
                    var onboarding = new OnboardingWindow(locService) { Owner = mainWindow };
                    ThemeService.TrySetDarkTitleBar(onboarding, themeService.AppliedTheme == AppliedTheme.Dark);
                    onboarding.ShowDialog();
                    var updated = settingsService.Current;
                    updated.OnboardingCompleted = true;
                    settingsService.SaveAsync(updated).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // Onboarding is a courtesy, never a startup blocker.
                    Log.Warning(ex, "First-run onboarding could not be shown.");
                }
            }

            Log.Information("DraftEngine started.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed.");

            if (upgradeVerification)
            {
                WriteUpgradeVerificationFailure(ex.GetType().Name);
                Shutdown(-1);
                return;
            }

            // The database bootstrap throws a curated InvalidOperationException; surface that
            // sentence so the user knows what to fix. Anything else could be any technical
            // failure (DI, XAML, IO), so only the generic line is shown - never ex.Message.
            var detail = ex is InvalidOperationException
                ? "\n\n" + ex.Message
                : string.Empty;

            MessageBox.Show(
                "DraftEngine could not start. The error has been logged." + detail,
                _appOptions?.App.DisplayName ?? "DraftEngine",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void VerifyUpgradeDatabase()
    {
        var dataDirectory = Environment.GetEnvironmentVariable("DRAFTENGINE_DATA_DIR");
        var expectedTitle = Environment.GetEnvironmentVariable("DRAFTENGINE_EXPECTED_CONTRACT_TITLE");
        var reportPath = Environment.GetEnvironmentVariable("DRAFTENGINE_UPGRADE_REPORT");
        if (string.IsNullOrWhiteSpace(dataDirectory) ||
            string.IsNullOrWhiteSpace(expectedTitle) ||
            string.IsNullOrWhiteSpace(reportPath))
        {
            throw new InvalidOperationException("Upgrade verification requires its test environment variables.");
        }

        using var scope = _host!.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<DraftEngineDbContext>();
        var contractSurvived = database.Contracts.Any(contract => contract.Title == expectedTitle);
        var pendingMigrations = database.Database.GetPendingMigrations().Count();
        var backupCount = Directory.GetFiles(
            dataDirectory,
            "draftengine.db.backup-*",
            SearchOption.TopDirectoryOnly).Length;

        var report = new
        {
            Status = contractSurvived && pendingMigrations == 0 && backupCount > 0 ? "passed" : "failed",
            ContractSurvived = contractSurvived,
            PendingMigrations = pendingMigrations,
            PreMigrationBackupCount = backupCount
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report));

        if (report.Status != "passed")
        {
            throw new InvalidOperationException("The database upgrade smoke check failed.");
        }
    }

    private static void WriteUpgradeVerificationFailure(string exceptionType)
    {
        var reportPath = Environment.GetEnvironmentVariable("DRAFTENGINE_UPGRADE_REPORT");
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                Status = "failed",
                ExceptionType = exceptionType
            }));
        }
        catch (IOException)
        {
            // The verification report is best-effort; the nonzero process exit remains authoritative.
        }
    }

    /// <summary>Applies the active culture to WPF text shaping and number substitution.</summary>
    private static void ApplyWpfLanguage(CultureInfo culture)
    {
        try
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        }
        catch (InvalidOperationException)
        {
            // Metadata already sealed (designer/host). The window style still binds Language.
        }
    }

    /// <summary>
    /// Disposes the host and flushes the log when the application exits.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception.");
        MessageBox.Show(
            "An unexpected error occurred. The error has been logged.\n\n" + e.Exception.Message,
            "DraftEngine",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.ExceptionObject as Exception, "Fatal unhandled exception.");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }
}
