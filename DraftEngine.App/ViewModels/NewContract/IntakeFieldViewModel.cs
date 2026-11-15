using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DraftEngine.App.Services.Localization;
using DraftEngine.Core.Data;

namespace DraftEngine.App.ViewModels.NewContract;

/// <summary>The kind of input rendered for an intake field on step 2 of the wizard.</summary>
public enum IntakeFieldKind
{
    /// <summary>Single-line text input.</summary>
    Text,

    /// <summary>Date picker input.</summary>
    Date,

    /// <summary>Drop-down with a fixed list of choices.</summary>
    Choice,

    /// <summary>Numeric text input (validated as a positive integer).</summary>
    Number,

    /// <summary>Country picker with an optional subdivision (governing law).</summary>
    Jurisdiction,

    /// <summary>Searchable country picker (courts/venue country).</summary>
    Country
}

/// <summary>
/// View model for one dynamic intake field on step 2 of the wizard.
/// Each field carries a stable key that is persisted as a <c>ContractField</c> row.
/// </summary>
public partial class IntakeFieldViewModel : ObservableObject
{
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private DateTime? _dateValue;
    [ObservableProperty] private string? _selectedChoice;
    [ObservableProperty] private string _countrySearch = string.Empty;
    [ObservableProperty] private CountryInfo? _selectedCountry;
    [ObservableProperty] private string _subdivisionText = string.Empty;
    private readonly string _fallbackLabel;
    private readonly ILocalizationService _localization;
    private readonly IReadOnlyList<string> _recentCountryCodes;

    /// <summary>Stable field key persisted as <c>ContractField.Key</c> (also used as the placeholder name).</summary>
    public string Key { get; }

    /// <summary>The field label shown above the input.</summary>
    public string Label
    {
        get
        {
            var key = $"Wizard.Field.{Key}";
            var value = _localization.GetString(key);
            return string.Equals(value, key, System.StringComparison.Ordinal) ? _fallbackLabel : value;
        }
    }

    /// <summary>Which input control to render for this field.</summary>
    public IntakeFieldKind Kind { get; }

    /// <summary>Whether the field must be filled in before the wizard can advance.</summary>
    public bool IsRequired { get; }

    /// <summary>The choices offered for <see cref="IntakeFieldKind.Choice"/> fields.</summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>Renders a text box (text or numeric input).</summary>
    public bool IsTextInput => Kind is IntakeFieldKind.Text or IntakeFieldKind.Number;

    /// <summary>Renders a date picker.</summary>
    public bool IsDateInput => Kind == IntakeFieldKind.Date;

    /// <summary>Renders a drop-down.</summary>
    public bool IsChoiceInput => Kind == IntakeFieldKind.Choice;

    /// <summary>Renders the country/subdivision picker.</summary>
    public bool IsJurisdictionInput => Kind == IntakeFieldKind.Jurisdiction;

    /// <summary>Renders the country-only picker.</summary>
    public bool IsCountryInput => Kind == IntakeFieldKind.Country;

    /// <summary>Countries matching the current search, recent selections first.</summary>
    public ObservableCollection<CountryInfo> FilteredCountries { get; } = new();

    /// <summary>Subdivisions offered for the selected country; empty means free text.</summary>
    public ObservableCollection<string> Subdivisions { get; } = new();

    /// <summary>True when the selected country has a certain built-in subdivision list.</summary>
    public bool HasSubdivisionList => Subdivisions.Count > 0;

    /// <summary>True when subdivision input should be free text.</summary>
    public bool UsesFreeTextSubdivision => Kind == IntakeFieldKind.Jurisdiction && !HasSubdivisionList;

    /// <summary>The selected country's ISO code, or an empty string.</summary>
    public string CountryCode => Kind == IntakeFieldKind.Country
        ? Value.Trim()
        : SelectedCountry?.Code ?? string.Empty;

    /// <summary>The selected subdivision text.</summary>
    public string Subdivision => SubdivisionText.Trim();

    /// <summary>The string value of the field, formatted for persistence and clause preview substitution.</summary>
    public string EffectiveValue => Kind switch
    {
        IntakeFieldKind.Date => DateValue?.ToString("MMMM d, yyyy") ?? string.Empty,
        IntakeFieldKind.Choice => SelectedChoice ?? string.Empty,
        IntakeFieldKind.Jurisdiction => JurisdictionResolver.Compose(
            SelectedCountry?.Code, SubdivisionText, CultureInfo.CurrentCulture.TwoLetterISOLanguageName),
        IntakeFieldKind.Country => Value.Trim(),
        _ => Value.Trim()
    };

    /// <summary>Creates an intake field view model.</summary>
    public IntakeFieldViewModel(
        string key,
        string label,
        IntakeFieldKind kind,
        bool isRequired,
        IReadOnlyList<string>? choices = null,
        string? defaultValue = null,
        DateTime? defaultDate = null,
        ILocalizationService? localization = null,
        string? defaultCountryCode = null,
        string? defaultSubdivision = null,
        IReadOnlyList<string>? recentCountryCodes = null)
    {
        _recentCountryCodes = recentCountryCodes ?? Array.Empty<string>();
        Key = key;
        _fallbackLabel = label;
        Kind = kind;
        IsRequired = isRequired;
        Choices = choices ?? Array.Empty<string>();
        Value = defaultValue ?? string.Empty;
        SelectedChoice = defaultValue;
        DateValue = defaultDate;
        _localization = localization ?? LocalizationSource.Current.Service ?? new LocalizationService();

        if (kind == IntakeFieldKind.Country)
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                Value = defaultCountryCode ?? CountryCatalog.DefaultCode();
            }

            SelectedCountry = CountryCatalog.Find(Value);
            RefreshCountries();
        }

        if (kind == IntakeFieldKind.Jurisdiction)
        {
            SubdivisionText = defaultSubdivision ?? string.Empty;
            SelectedCountry = CountryCatalog.Find(defaultCountryCode) ?? CountryCatalog.Find(CountryCatalog.DefaultCode());
            RefreshCountries();
            RefreshSubdivisions();
        }
    }

    public void RefreshLocalization() =>
        OnPropertyChanged(nameof(Label));

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(CountryCode));
    }

    partial void OnDateValueChanged(DateTime? value) => OnPropertyChanged(nameof(IsValid));

    partial void OnSelectedChoiceChanged(string? value) => OnPropertyChanged(nameof(IsValid));

    partial void OnCountrySearchChanged(string value) => RefreshCountries();

    partial void OnSelectedCountryChanged(CountryInfo? value)
    {
        if (Kind == IntakeFieldKind.Country && value is not null)
        {
            Value = value.Code;
        }

        RefreshSubdivisions();
        OnPropertyChanged(nameof(CountryCode));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(EffectiveValue));
    }

    partial void OnSubdivisionTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(EffectiveValue));
    }

    private void RefreshCountries()
    {
        var matches = CountryCatalog.Search(CountrySearch, CultureInfo.CurrentCulture);
        if (string.IsNullOrWhiteSpace(CountrySearch))
        {
            var recents = _recentCountryCodes
                .Select(CountryCatalog.Find)
                .Where(country => country is not null)
                .Select(country => country!)
                .ToList();
            matches = recents.Concat(matches.Where(match => recents.All(recent => recent.Code != match.Code))).ToList();
        }

        FilteredCountries.Clear();
        foreach (var country in matches)
        {
            FilteredCountries.Add(country);
        }
    }

    private void RefreshSubdivisions()
    {
        Subdivisions.Clear();
        foreach (var subdivision in SubdivisionCatalog.For(SelectedCountry?.Code))
        {
            Subdivisions.Add(subdivision);
        }

        OnPropertyChanged(nameof(HasSubdivisionList));
        OnPropertyChanged(nameof(UsesFreeTextSubdivision));
    }

    /// <summary>Whether the field currently satisfies its validation rule.</summary>
    public bool IsValid => !IsRequired || Kind switch
    {
        IntakeFieldKind.Date => DateValue is not null,
        IntakeFieldKind.Choice => !string.IsNullOrWhiteSpace(SelectedChoice),
        IntakeFieldKind.Jurisdiction => SelectedCountry is not null,
        IntakeFieldKind.Country => !string.IsNullOrWhiteSpace(Value),
        IntakeFieldKind.Number => int.TryParse(
            Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var number) && number > 0,
        _ => !string.IsNullOrWhiteSpace(Value)
    };
}
