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
    private ResourceDictionary? _currentDictionary;

    public ThemeService(IAppSettingsService settings, ISystemThemeProvider systemTheme)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _systemTheme = systemTheme ?? throw new ArgumentNullException(nameof(systemTheme));
        _systemTheme.SystemThemeChanged += OnSystemThemeChanged;
    }

    public ThemeMode Mode { get; private set; } = ThemeMode.System;
    public AppliedTheme AppliedTheme { get; private set; } = AppliedTheme.Light;
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
        if (Mode == ThemeMode.System)
        {
            Apply(Mode, persist: false);
        }
    }

    private void Apply(ThemeMode mode, bool persist)
    {
        Mode = mode;
        var applied = ThemeResolver.Resolve(mode, _systemTheme.SystemUsesLightMode());
        if (applied == AppliedTheme && _currentDictionary is not null && !persist)
        {
            // Still refresh title bars after startup.
            UpdateTitleBars(applied);
            return;
        }

        AppliedTheme = applied;
        var uri = new Uri(
            applied == AppliedTheme.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml",
            UriKind.Relative);

        try
        {
            var dictionary = new ResourceDictionary { Source = uri };
            var merged = Application.Current?.Resources.MergedDictionaries;
            if (merged is not null)
            {
                if (_currentDictionary is not null)
                {
                    merged.Remove(_currentDictionary);
                }

                // Theme first so Typography/Styles resolve against its brushes.
                merged.Insert(0, dictionary);
                _currentDictionary = dictionary;
            }

            UpdateTitleBars(applied);
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The theme resource dictionary could not be applied.");
        }
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
        ( _systemTheme as IDisposable)?.Dispose();
    }
}
