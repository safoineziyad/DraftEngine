using System.Windows;

namespace DraftEngine.App.Services;

/// <summary>
/// Shows small modal dialogs to the user (used by the contract wizard for friendly error reporting).
/// </summary>
public interface IWizardDialogService
{
    /// <summary>Shows an error dialog with a friendly title.</summary>
    void ShowError(string message, string title = "DraftEngine");
}

/// <summary>
/// Default <see cref="IWizardDialogService"/> implementation backed by the WPF <see cref="MessageBox"/>.
/// </summary>
public class WizardDialogService : IWizardDialogService
{
    /// <inheritdoc />
    public void ShowError(string message, string title = "DraftEngine")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
