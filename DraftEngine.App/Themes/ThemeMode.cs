namespace DraftEngine.App.Themes;

/// <summary>User-selectable theme. <see cref="System"/> follows Windows.</summary>
public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>Resolved theme actually applied to the application.</summary>
public enum AppliedTheme
{
    Light,
    Dark
}

/// <summary>Pure theme resolution and parsing, independent of WPF so it is testable.</summary>
public static class ThemeResolver
{
    public static ThemeMode Parse(string? value) =>
        Enum.TryParse<ThemeMode>(value, ignoreCase: true, out var mode) ? mode : ThemeMode.System;

    public static string ToStorageValue(ThemeMode mode) => mode.ToString();

    /// <summary>
    /// Resolves the applied theme. <paramref name="systemUsesLight"/> is null when the
    /// registry value is missing; Windows' documented default is Light.
    /// </summary>
    public static AppliedTheme Resolve(ThemeMode mode, bool? systemUsesLight) => mode switch
    {
        ThemeMode.Dark => AppliedTheme.Dark,
        ThemeMode.Light => AppliedTheme.Light,
        _ => systemUsesLight == false ? AppliedTheme.Dark : AppliedTheme.Light
    };
}

/// <summary>Reads and observes the Windows app-mode preference.</summary>
public interface ISystemThemeProvider
{
    /// <summary>Null when the registry value is missing (treated as Light).</summary>
    bool? SystemUsesLightMode();

    event EventHandler? SystemThemeChanged;
}

/// <summary>Reads HKCU\...\Themes\Personalize\AppsUseLightTheme and listens for changes.</summary>
public sealed class RegistrySystemThemeProvider : ISystemThemeProvider, IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    public event EventHandler? SystemThemeChanged;

    public RegistrySystemThemeProvider()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool? SystemUsesLightMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            var value = key?.GetValue(ValueName);
            return value is int integer ? integer != 0 : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is Microsoft.Win32.UserPreferenceCategory.General
            or Microsoft.Win32.UserPreferenceCategory.VisualStyle)
        {
            SystemThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
