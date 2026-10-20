namespace DraftEngine.App.Services;

/// <summary>
/// Navigates between application pages by string key.
/// </summary>
public interface INavigationService
{
    /// <summary>Raised when the current view model changes.</summary>
    event Action<object?>? CurrentViewModelChanged;

    /// <summary>The view model of the page currently displayed.</summary>
    object? CurrentViewModel { get; }

    /// <summary>
    /// Navigates to the page registered under <paramref name="key"/>. Returns false for unknown keys.
    /// When the target view model implements <see cref="INavigationAware"/> it receives
    /// <paramref name="parameter"/> (e.g. a contract id) before the page is shown.
    /// </summary>
    bool Navigate(string key, object? parameter = null);

    /// <summary>The key of the page currently displayed.</summary>
    string CurrentKey { get; }
}

/// <summary>Implemented by page view models that need a parameter when navigated to.</summary>
public interface INavigationAware
{
    /// <summary>Called by the navigation service right before the page becomes visible.</summary>
    void OnNavigatedTo(object? parameter);
}
