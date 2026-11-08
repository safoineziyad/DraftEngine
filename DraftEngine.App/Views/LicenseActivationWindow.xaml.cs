using System.Windows;
using DraftEngine.App.Services.Licensing;

namespace DraftEngine.App.Views;

/// <summary>
/// Modal activation gate shown before the main window when offline licensing is enabled and
/// no usable license exists. It performs no network call.
/// </summary>
public partial class LicenseActivationWindow : Window
{
    private readonly ILicenseService _licenseService;

    /// <summary>Creates the window for the given license service.</summary>
    public LicenseActivationWindow(ILicenseService licenseService)
    {
        InitializeComponent();
        _licenseService = licenseService;
    }

    private async void OnActivateClick(object sender, RoutedEventArgs e)
    {
        var key = LicenseKeyBox.Text?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            StatusText.Text = "Paste the license key you received.";
            return;
        }

        try
        {
            var evaluation = await _licenseService.ActivateAsync(key);
            if (evaluation.IsUsable)
            {
                DialogResult = true;
                return;
            }

            StatusText.Text = evaluation.Message;
        }
        catch
        {
            StatusText.Text = "The license could not be activated. Check the key and try again.";
        }
    }

    private void OnQuitClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
