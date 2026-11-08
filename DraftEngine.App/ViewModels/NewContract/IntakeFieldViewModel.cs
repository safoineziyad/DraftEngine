using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DraftEngine.App.Services.Localization;

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
    Number
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
    private readonly string _fallbackLabel;
    private readonly ILocalizationService _localization;

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

    /// <summary>The string value of the field, formatted for persistence and clause preview substitution.</summary>
    public string EffectiveValue => Kind switch
    {
        IntakeFieldKind.Date => DateValue?.ToString("MMMM d, yyyy") ?? string.Empty,
        IntakeFieldKind.Choice => SelectedChoice ?? string.Empty,
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
        ILocalizationService? localization = null)
    {
        Key = key;
        _fallbackLabel = label;
        Kind = kind;
        IsRequired = isRequired;
        Choices = choices ?? Array.Empty<string>();
        Value = defaultValue ?? string.Empty;
        SelectedChoice = defaultValue;
        DateValue = defaultDate;
        _localization = localization ?? LocalizationSource.Current.Service ?? new LocalizationService();
    }

    public void RefreshLocalization() =>
        OnPropertyChanged(nameof(Label));

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(IsValid));
    partial void OnDateValueChanged(DateTime? value) => OnPropertyChanged(nameof(IsValid));
    partial void OnSelectedChoiceChanged(string? value) => OnPropertyChanged(nameof(IsValid));

    /// <summary>Whether the field currently satisfies its validation rule.</summary>
    public bool IsValid => !IsRequired || Kind switch
    {
        IntakeFieldKind.Date => DateValue is not null,
        IntakeFieldKind.Choice => !string.IsNullOrWhiteSpace(SelectedChoice),
        IntakeFieldKind.Number => int.TryParse(
            Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var number) && number > 0,
        _ => !string.IsNullOrWhiteSpace(Value)
    };
}
