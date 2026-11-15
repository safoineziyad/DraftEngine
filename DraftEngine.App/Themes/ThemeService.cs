using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DraftEngine.App.Services.Settings;
using Serilog;

namespace DraftEngine.App.Themes;

/// <summary>Applies Light/Dark/Follow-Windows at runtime and persists the choice.</summary>
public interface IThemeService
{
    ThemeMode Mode { get; }
    AppliedTheme AppliedTheme { get; }
    event EventHandler? ThemeChanged;

    /// <summary>Applies the saved mode without writing settings (startup).</summary>
    void Initialize(string? storedMode);

    /// <summary>Changes and persists the user's choice.</summary>
    Task SetModeAsync(ThemeMode mode);
}

/// <summary>
/// Replaces the theme ResourceDictionary in <see cref="Application.Resources"/> while keeping
/// styles and typography. Dark title bars use DwmSetWindowAttribute and fail silently.
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly IAppSettingsService _settings;
    private readonly ISystemThemeProvider _systemTheme;
    private readonly Func<bool> _highContrastProvider;
    private ResourceDictionary? _currentDictionary;
    private bool _highContrast;

    public ThemeService(
        IAppSettingsService settings,
        ISystemThemeProvider systemTheme,
        Func<bool>? highContrastProvider = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _systemTheme = systemTheme ?? throw new ArgumentNullException(nameof(systemTheme));
        _highContrastProvider = highContrastProvider ?? (() => SystemParameters.HighContrast);
        _systemTheme.SystemThemeChanged += OnSystemThemeChanged;
        try
        {
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        }
        catch (Exception)
        {
            // Static system-parameter events are best-effort.
        }
    }

    public ThemeMode Mode { get; private set; } = ThemeMode.System;
    public AppliedTheme AppliedTheme { get; private set; } = AppliedTheme.Light;

    /// <summary>True when the Windows High Contrast scheme is driving the palette.</summary>
    public bool IsHighContrast => _highContrast;
    public event EventHandler? ThemeChanged;

    public void Initialize(string? storedMode)
    {
        Mode = ThemeResolver.Parse(storedMode);
        Apply(Mode, persist: false);
    }

    public async Task SetModeAsync(ThemeMode mode)
    {
        Apply(mode, persist: true);

        var current = _settings.Current;
        current.Theme = ThemeResolver.ToStorageValue(mode);
        await _settings.SaveAsync(current);
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        // High Contrast can change while an explicit Light/Dark choice is active, so
        // always re-evaluate; Follow-Windows also re-resolves its light/dark value.
        if (Mode == ThemeMode.System || IsHighContrast != SafeHighContrast())
        {
            Apply(Mode, persist: false);
        }
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
        {
            Apply(Mode, persist: false);
        }
    }

    private bool SafeHighContrast()
    {
        try
        {
            return _highContrastProvider();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Apply(ThemeMode mode, bool persist)
    {
        Mode = mode;
        var applied = ThemeResolver.Resolve(mode, _systemTheme.SystemUsesLightMode());
        var highContrast = SafeHighContrast();
        if (applied == AppliedTheme && highContrast == _highContrast && _currentDictionary is not null && !persist)
        {
            // Still refresh title bars after startup.
            UpdateTitleBars(applied);
            return;
        }

        AppliedTheme = applied;
        _highContrast = highContrast;
        var themeFile = highContrast
            ? "Themes/HighContrast.xaml"
            : applied == AppliedTheme.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml";
        var uri = new Uri(themeFile, UriKind.Relative);

        try
        {
            var dictionary = new ResourceDictionary { Source = uri };
            var merged = Application.Current?.Resources.MergedDictionaries;
            if (merged is not null)
            {
                // Remove every theme dictionary, including the static Light fallback merged
                // in App.xaml and any previously applied dictionary, so a dark theme can
                // never be shadowed by the light one (later merged dictionaries win).
                for (var index = merged.Count - 1; index >= 0; index--)
                {
                    if (IsThemeDictionary(merged[index]))
                    {
                        merged.RemoveAt(index);
                    }
                }

                // Theme first so Typography/Styles resolve against its brushes.
                merged.Insert(0, dictionary);
                _currentDictionary = dictionary;
            }

            // Never force a dark title bar in High Contrast; the system scheme owns it.
            UpdateTitleBars(highContrast ? AppliedTheme.Light : applied);
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The theme resource dictionary could not be applied.");
        }
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source is not null &&
            (source.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) ||
             source.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
             source.EndsWith("Themes/HighContrast.xaml", StringComparison.OrdinalIgnoreCase));
    }

    private static void UpdateTitleBars(AppliedTheme theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (Window window in Application.Current.Windows)
        {
            TrySetDarkTitleBar(window, theme == AppliedTheme.Dark);
        }
    }

    /// <summary>Applies the immersive dark title bar; never throws.</summary>
    public static void TrySetDarkTitleBar(Window window, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                window.SourceInitialized += (_, _) => TrySetDarkTitleBar(window, dark);
                return;
            }

            var value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
            }
        }
        catch (Exception)
        {
            // Older Windows builds or a missing DWM: keep the normal title bar.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public void Dispose()
    {
        _systemTheme.SystemThemeChanged -= OnSystemThemeChanged;
        try
        {
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        }
        catch (Exception)
        {
            // Ignore teardown races.
        }

        ( _systemTheme as IDisposable)?.Dispose();
    }
}
