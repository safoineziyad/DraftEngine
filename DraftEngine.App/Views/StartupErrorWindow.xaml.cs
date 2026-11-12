using System;
using System.Windows;
using System.Windows.Automation;
using DraftEngine.App.Services.Localization;

namespace DraftEngine.App.Views;

/// <summary>
/// Shown when startup fails, including a failed database migration whose pre-upgrade
/// snapshot was restored. All copy is localized; "Copy diagnostics" copies only the
/// version, Windows, .NET and exception types built by
/// <see cref="Services.StartupDiagnostics"/>, never user content.
/// </summary>
public partial class StartupErrorWindow : Window
{
    private readonly ILocalizationService? _localization;

    /// <summary>Creates the window for the given localization service and diagnostics text.</summary>
    public StartupErrorWindow(ILocalizationService? localization, bool databaseRestored, string diagnostics)
    {
        InitializeComponent();
        _localization = localization;

        Title = Text("Startup.Error.Title", "DraftEngine could not start");
        HeaderText.Text = Title;
        BodyText.Text = databaseRestored
            ? Text("Startup.Error.BodyRestored",
                "The database upgrade could not be completed, so the pre-upgrade backup was restored. " +
                "Your data is unchanged. Restart the application; if the problem persists, contact support.")
            : Text("Startup.Error.Body",
                "DraftEngine could not open its database. Your data was not changed. " +
                "Restart the application; if the problem persists, contact support.");
        DiagnosticsLabel.Text = Text("Startup.Error.DiagnosticsLabel",
            "Diagnostics (version, Windows, .NET and error type only; no contract data)");
        DiagnosticsBox.Text = diagnostics;

        CopyButton.Content = Text("Startup.Error.CopyDiagnostics", "Copy diagnostics");
        AutomationProperties.SetName(CopyButton, (string)CopyButton.Content);
        CloseButton.Content = Text("Startup.Error.Close", "Close");
        AutomationProperties.SetName(CloseButton, (string)CloseButton.Content);
    }

    private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DiagnosticsBox.Text);
            StatusText.Text = Text("Startup.Error.DiagnosticsCopied", "Diagnostics copied.");
        }
        catch (Exception)
        {
            StatusText.Text = Text("Startup.Error.ClipboardUnavailable", "The clipboard is unavailable.");
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private string Text(string key, string fallback) =>
        _localization is null ? fallback : _localization.GetString(key);
}
