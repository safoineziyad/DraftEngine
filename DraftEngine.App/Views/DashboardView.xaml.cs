using System.Windows.Controls;

namespace DraftEngine.App.Views;

/// <summary>
/// Dashboard page view.
/// </summary>
public partial class DashboardView : UserControl
{
    /// <summary>Creates the dashboard view (used by XAML templates; DataContext is supplied by the parent ContentControl).</summary>
    public DashboardView()
    {
        InitializeComponent();
    }
}
