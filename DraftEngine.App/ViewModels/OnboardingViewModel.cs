using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services.Localization;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// Content for the three-screen first-run onboarding dialog. It deliberately contains no
/// AI calls or network access.
/// </summary>
public partial class OnboardingViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private bool _isApplyingLanguage;

    /// <summary>Creates the onboarding view model, optionally with localization.</summary>
    public OnboardingViewModel(ILocalizationService? localization = null)
    {
        // Fall back to a default English service when the app has not initialized the
        // shared source (e.g. design-time or a unit test); never render raw keys.
        _localization = localization ?? LocalizationSource.Current.Service ?? new LocalizationService();
        _localization.LanguageChanged += OnLanguageChanged;
        _isApplyingLanguage = true;
        SelectedLanguage = _localization.CurrentLanguage;
        _isApplyingLanguage = false;
    }

    /// <summary>Languages offered on the first onboarding screen.</summary>
    public IReadOnlyList<SupportedLanguage> SupportedLanguages => _localization.SupportedLanguages;

    /// <summary>Language code selected in the first-screen picker.</summary>
    [ObservableProperty]
    private string selectedLanguage = "en";

    private static readonly OnboardingStep[] Steps =
    [
        new(
            "Onboarding.Step1.Title",
            "Onboarding.Step1.Description",
            [
                "Onboarding.Step1.Bullet1",
                "Onboarding.Step1.Bullet2",
                "Onboarding.Step1.Bullet3",
            ]),
        new(
            "Onboarding.Step2.Title",
            "Onboarding.Step2.Description",
            [
                "Onboarding.Step2.Bullet1",
                "Onboarding.Step2.Bullet2",
                "Onboarding.Step2.Bullet3",
            ]),
        new(
            "Onboarding.Step3.Title",
            "Onboarding.Step3.Description",
            [
                "Onboarding.Step3.Bullet1",
                "Onboarding.Step3.Bullet2",
                "Onboarding.Step3.Bullet3",
            ]),
    ];

    /// <summary>Raised when the user finishes or skips the onboarding flow.</summary>
    public event EventHandler? Completed;

    [ObservableProperty]
    private int stepIndex;

    /// <summary>Zero-based index of the current screen.</summary>
    public int StepCount => Steps.Length;

    /// <summary>Human-readable progress label, e.g. "Step 1 of 3".</summary>
    public string StepLabel => _localization.GetString("Onboarding.StepLabel", StepIndex + 1, StepCount);

    /// <summary>The current screen's title.</summary>
    public string StepTitle => _localization.GetString(Steps[StepIndex].TitleKey);

    /// <summary>The current screen's introductory paragraph.</summary>
    public string StepDescription => _localization.GetString(Steps[StepIndex].DescriptionKey);

    /// <summary>The current screen's short bullet list.</summary>
    public IReadOnlyList<string> StepBullets =>
        Steps[StepIndex].BulletKeys.Select(_localization.GetString).ToArray();

    /// <summary>True on the first screen (the Back button is hidden).</summary>
    public bool IsFirst => StepIndex == 0;

    /// <summary>True on the last screen (the primary button becomes "Get started").</summary>
    public bool IsLast => StepIndex == Steps.Length - 1;

    /// <summary>Inverse of <see cref="IsFirst"/>, for XAML visibility bindings.</summary>
    public bool IsNotFirst => !IsFirst;

    /// <summary>The primary button label for the current screen.</summary>
    public string PrimaryButtonText =>
        _localization.GetString(IsLast ? "Common.GetStarted" : "Common.Next");

    /// <summary>Moves to the next screen, or finishes on the last screen.</summary>
    [RelayCommand]
    private void Next()
    {
        if (IsLast)
        {
            Completed?.Invoke(this, EventArgs.Empty);
            return;
        }

        StepIndex++;
    }

    /// <summary>Moves back one screen.</summary>
    [RelayCommand]
    private void Back()
    {
        if (!IsFirst)
        {
            StepIndex--;
        }
    }

    /// <summary>Closes onboarding without walking through the remaining screens.</summary>
    [RelayCommand]
    private void Skip() => Completed?.Invoke(this, EventArgs.Empty);

    partial void OnStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StepDescription));
        OnPropertyChanged(nameof(StepBullets));
        OnPropertyChanged(nameof(IsFirst));
        OnPropertyChanged(nameof(IsLast));
        OnPropertyChanged(nameof(IsNotFirst));
        OnPropertyChanged(nameof(PrimaryButtonText));
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (_isApplyingLanguage || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _ = _localization.SetLanguageAsync(value);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SupportedLanguages));
        OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StepDescription));
        OnPropertyChanged(nameof(StepBullets));
        OnPropertyChanged(nameof(PrimaryButtonText));

        if (!string.Equals(SelectedLanguage, _localization.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _isApplyingLanguage = true;
            SelectedLanguage = _localization.CurrentLanguage;
            _isApplyingLanguage = false;
        }
    }

    private sealed record OnboardingStep(
        string TitleKey,
        string DescriptionKey,
        IReadOnlyList<string> BulletKeys);
}
