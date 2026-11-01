using System.Windows;
using DraftEngine.App.ViewModels;

namespace DraftEngine.App.Views;

/// <summary>Modal first-run onboarding dialog. Closing it also counts as skipping.</summary>
public partial class OnboardingWindow : Window
{
    /// <summary>Initializes the dialog and wires its completion event to the window result.</summary>
    public OnboardingWindow()
    {
        InitializeComponent();
        DataContext = new OnboardingViewModel();

        if (DataContext is OnboardingViewModel viewModel)
        {
            viewModel.Completed += (_, _) => DialogResult = true;
        }
    }
}
