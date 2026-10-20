using System.Windows;
using System.Windows.Controls;

namespace DraftEngine.App.Services.Settings;

/// <summary>
/// Attached property that lets a <see cref="PasswordBox"/> participate in MVVM data binding.
/// WPF's <c>PasswordBox.Password</c> is not a dependency property, so this helper mirrors it
/// onto a bindable attached property. Used by the Settings page for the OpenAI API key:
/// the plaintext only flows between the control and the view model's memory — never to disk.
/// </summary>
public static class PasswordBoxHelper
{
    private static readonly DependencyProperty PasswordBoxInstanceProperty = DependencyProperty.RegisterAttached(
        "PasswordBoxInstance", typeof(PasswordBox), typeof(PasswordBoxHelper), new PropertyMetadata(null));

    /// <summary>Bindable, two-way mirror of <see cref="PasswordBox.Password"/>.</summary>
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword",
        typeof(string),
        typeof(PasswordBoxHelper),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    /// <summary>Gets the mirrored password value.</summary>
    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);

    /// <summary>Sets the mirrored password value (also updates the box).</summary>
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    private static bool _isUpdating;

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;

        // Subscribe exactly once per box.
        box.PasswordChanged -= OnPasswordChanged;
        if (!ReferenceEquals(box.GetValue(PasswordBoxInstanceProperty), box))
        {
            box.SetValue(PasswordBoxInstanceProperty, box);
            box.PasswordChanged += OnPasswordChanged;
        }

        var newValue = (string?)e.NewValue ?? string.Empty;
        if (box.Password != newValue)
            box.Password = newValue;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box || _isUpdating) return;

        _isUpdating = true;
        try
        {
            SetBoundPassword(box, box.Password);
        }
        finally
        {
            _isUpdating = false;
        }
    }
}
