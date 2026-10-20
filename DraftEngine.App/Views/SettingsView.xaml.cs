using System.Windows.Controls;

namespace DraftEngine.App.Views;

/// <summary>
/// Settings page view.
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>Creates the settings view (used by XAML templates; DataContext is supplied by the parent ContentControl).</summary>
    public SettingsView()
    {
        InitializeComponent();
    }
}
