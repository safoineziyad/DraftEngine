using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace DraftEngine.App.Services.Localization;

/// <summary>
/// Single bindable source for live localization. Bind to its indexer
/// (<c>[Key]</c>) or to <see cref="FlowDirection"/>; raising the WPF indexer
/// notification ("Item[]") refreshes every bound string without a restart.
/// </summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    private ILocalizationService? _service;

    private LocalizationSource()
    {
    }

    /// <summary>The application-wide source used by <see cref="TExtension"/> and window styles.</summary>
    public static LocalizationSource Current { get; } = new();

    /// <summary>Attaches the resolved localization service and starts listening for language changes.</summary>
    public static void Initialize(ILocalizationService service) => Current.Attach(service);

    /// <summary>The attached service, if initialization has run.</summary>
    public ILocalizationService? Service => _service;

    /// <summary>Live localized value for a resource key.</summary>
    public string this[string key] =>
        string.IsNullOrWhiteSpace(key) || _service is null ? string.Empty : _service.GetString(key);

    /// <summary>Current language code, for bindings that need it.</summary>
    public string CurrentLanguage => _service?.CurrentLanguage ?? "en";

    /// <summary>Text direction for the active language.</summary>
    public FlowDirection FlowDirection => _service?.GetFlowDirection() ?? FlowDirection.LeftToRight;

    /// <summary>True when the active language is right-to-left.</summary>
    public bool IsRtl => _service?.IsRtl() ?? false;

    /// <summary>Culture language tag, used to refresh WPF text shaping/number substitution.</summary>
    public XmlLanguage Language =>
        XmlLanguage.GetLanguage((_service?.Culture ?? CultureInfo.GetCultureInfo("en")).IetfLanguageTag);

    /// <summary>Languages offered by the pickers.</summary>
    public IReadOnlyList<SupportedLanguage> SupportedLanguages =>
        _service?.SupportedLanguages ?? Array.Empty<SupportedLanguage>();

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Attaches a service (idempotent) and refreshes all bindings.</summary>
    public void Attach(ILocalizationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (ReferenceEquals(_service, service))
        {
            return;
        }

        if (_service is not null)
        {
            _service.LanguageChanged -= OnLanguageChanged;
        }

        _service = service;
        _service.LanguageChanged += OnLanguageChanged;
        RaiseAll();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RaiseAll();

    private void RaiseAll()
    {
        // WPF binding to an indexer listens for "Item[]" (and "Item").
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRtl)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SupportedLanguages)));
    }
}

/// <summary>Applies <c>{0}</c>-style arguments to an already-resolved template.</summary>
public sealed class LocalizedFormatConverter : IValueConverter
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly LocalizedFormatConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string template || parameter is not string[] args || args.Length == 0)
        {
            return value ?? string.Empty;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Markup extension <c>{loc:T Key}</c>. It returns a binding to
/// <see cref="LocalizationSource"/> so the text refreshes live when the language
/// changes. Optional format arguments: <c>{loc:T Key, Args='1,2'}</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class TExtension : MarkupExtension
{
    /// <summary>Resource key, e.g. <c>Nav.Dashboard</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Optional format arguments for templates containing <c>{0}</c>, <c>{1}</c>, …</summary>
    public string[]? Args { get; set; }

    /// <summary>Creates an empty extension (XAML sets <see cref="Key"/>).</summary>
    public TExtension()
    {
    }

    /// <summary>Creates an extension for a key.</summary>
    public TExtension(string key) => Key = key;

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return string.Empty;
        }

        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationSource.Current,
            Mode = BindingMode.OneWay
        };

        if (Args is { Length: > 0 })
        {
            binding.Converter = LocalizedFormatConverter.Instance;
            binding.ConverterParameter = Args;
        }

        return binding.ProvideValue(serviceProvider);
    }

    /// <summary>C# helper for code that cannot use XAML: <c>TExtension.Get("Key")</c>.</summary>
    public static string Get(string key) => LocalizationSource.Current[key];

    /// <summary>C# helper with format arguments.</summary>
    public static string Format(string key, params object[] args)
    {
        var service = LocalizationSource.Current.Service;
        return service is null ? key : service.GetString(key, args);
    }
}
