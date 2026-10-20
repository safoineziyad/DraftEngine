using System.Windows;

namespace DraftEngine.App;

/// <summary>
/// Main application window. MVVM: no logic here, the DataContext is set at startup.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}
