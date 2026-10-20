using System.IO;
using System.Windows;
using System.Windows.Threading;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services;
using DraftEngine.App.ViewModels;
using DraftEngine.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

            var mainWindow = new MainWindow
            {
                DataContext = _host.Services.GetRequiredService<MainViewModel>()
            };
            MainWindow = mainWindow;
            mainWindow.Show();

            Log.Information("DraftEngine started.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed.");
            MessageBox.Show(
                "DraftEngine could not start. The error has been logged.\n\n" + ex.Message,
                _appOptions?.App.DisplayName ?? "DraftEngine",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
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
