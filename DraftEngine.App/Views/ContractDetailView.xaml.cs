using System.Windows.Controls;

namespace DraftEngine.App.Views;

/// <summary>
/// Contract detail page view.
/// </summary>
public partial class ContractDetailView : UserControl
{
    /// <summary>Creates the contract detail view (used by XAML templates; DataContext is supplied by the parent ContentControl).</summary>
    public ContractDetailView()
    {
        InitializeComponent();
    }
}
