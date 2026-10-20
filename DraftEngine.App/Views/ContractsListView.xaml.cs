using System.Windows.Controls;

namespace DraftEngine.App.Views;

/// <summary>
/// Contracts list page view.
/// </summary>
public partial class ContractsListView : UserControl
{
    /// <summary>Creates the contracts list view (used by XAML templates; DataContext is supplied by the parent ContentControl).</summary>
    public ContractsListView()
    {
        InitializeComponent();
    }
}
