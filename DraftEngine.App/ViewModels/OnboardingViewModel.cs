using System;
using System.Collections.Generic;
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
    private readonly ILocalizationService? _localization;
    private bool _isApplyingLanguage;

    /// <summary>Creates the onboarding view model, optionally with localization.</summary>
    public OnboardingViewModel(ILocalizationService? localization = null)
    {
        _localization = localization ?? LocalizationSource.Current.Service;
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
            _isApplyingLanguage = true;
            SelectedLanguage = _localization.CurrentLanguage;
            _isApplyingLanguage = false;
        }
    }

    /// <summary>Languages offered on the first onboarding screen.</summary>
    public IReadOnlyList<SupportedLanguage> SupportedLanguages =>
        _localization?.SupportedLanguages ?? Array.Empty<SupportedLanguage>();

    /// <summary>Language code selected in the first-screen picker.</summary>
    [ObservableProperty]
    private string selectedLanguage = "en";

    private static readonly OnboardingStep[] Steps =
    [
        new(
            "Welcome to DraftEngine",
            "Draft polished contracts from a guided intake instead of starting from a blank page.",
            [
                "Choose a contract type and answer a short set of questions.",
                "Review the suggested clauses before anything is generated.",
                "Export the finished contract as DOCX or PDF.",
            ]),
        new(
            "How a contract comes together",
            "The wizard walks you through the same steps every time, so nothing important is skipped.",
            [
                "Pick the contract type that matches the deal.",
                "Fill in the parties, dates and commercial terms.",
                "Deselect optional clauses, then generate and export.",
            ]),
        new(
            "Your data stays local",
            "Contracts, intake answers and chat history are stored on this computer, encrypted with your Windows user profile.",
            [
                "AI editing only sends text to your configured AI provider when you use it.",
                "There is no telemetry, account sync or background upload.",
                "Export important contracts before changing Windows users or computers.",
            ]),
    ];

    /// <summary>Raised when the user finishes or skips the onboarding flow.</summary>
    public event EventHandler? Completed;

    [ObservableProperty]
    private int stepIndex;

    /// <summary>Zero-based index of the current screen.</summary>
    public int StepCount => Steps.Length;

    /// <summary>Human-readable progress label, e.g. "Step 1 of 3".</summary>
    public string StepLabel => $"Step {StepIndex + 1} of {StepCount}";

    /// <summary>The current screen's title.</summary>
    public string StepTitle => Steps[StepIndex].Title;

    /// <summary>The current screen's introductory paragraph.</summary>
    public string StepDescription => Steps[StepIndex].Description;

    /// <summary>The current screen's short bullet list.</summary>
    public IReadOnlyList<string> StepBullets => Steps[StepIndex].Bullets;

    /// <summary>True on the first screen (the Back button is hidden).</summary>
    public bool IsFirst => StepIndex == 0;

    /// <summary>True on the last screen (the primary button becomes "Get started").</summary>
    public bool IsLast => StepIndex == Steps.Length - 1;

    /// <summary>Inverse of <see cref="IsFirst"/>, for XAML visibility bindings.</summary>
    public bool IsNotFirst => !IsFirst;

    /// <summary>The primary button label for the current screen.</summary>
    public string PrimaryButtonText => IsLast ? "Get started" : "Next";

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
        if (_isApplyingLanguage || _localization is null || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _ = _localization.SetLanguageAsync(value);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SupportedLanguages));

        if (_localization is not null &&
            !string.Equals(SelectedLanguage, _localization.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _isApplyingLanguage = true;
            SelectedLanguage = _localization.CurrentLanguage;
            _isApplyingLanguage = false;
        }
    }

    private sealed record OnboardingStep(string Title, string Description, IReadOnlyList<string> Bullets);
}
