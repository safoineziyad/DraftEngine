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

    /// <summary>Navigates to the page registered under <paramref name="key"/>. Returns false for unknown keys.</summary>
    bool Navigate(string key);

    /// <summary>The key of the page currently displayed.</summary>
    string CurrentKey { get; }
}
