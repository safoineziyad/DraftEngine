using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DraftEngine.App.Converters;

/// <summary>
/// Inverts a boolean and maps it to <see cref="Visibility"/>: true collapses the element,
/// false makes it visible. Used by panels that show a hint while their list is empty.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <summary>Converts true to <see cref="Visibility.Collapsed"/> and false to <see cref="Visibility.Visible"/>.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is false ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Converts <see cref="Visibility.Visible"/> back to false; anything else back to true.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not Visibility.Visible;
}
