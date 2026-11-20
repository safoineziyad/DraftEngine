using System.Windows;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Profiles;
using DraftEngine.App.Themes;
using DraftEngine.App.ViewModels;

namespace DraftEngine.App.Views;

/// <summary>Modal first-run onboarding dialog. Closing it also counts as skipping.</summary>
public partial class OnboardingWindow : Window
{
    /// <summary>Initializes the dialog and wires its completion event to the window result.</summary>
    public OnboardingWindow(
        ILocalizationService? localization = null,
        IThemeService? themeService = null,
        IProfileService? profileService = null)
    {
        InitializeComponent();
        DataContext = new OnboardingViewModel(localization, themeService, profileService);

        if (DataContext is OnboardingViewModel viewModel)
        {
            viewModel.Completed += (_, _) => DialogResult = true;
        }
    }
}
