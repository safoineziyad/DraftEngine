using System;
using System.Threading.Tasks;
using DraftEngine.App.Services.Settings;

namespace DraftEngine.App.Services.Profiles;

/// <summary>Reads and updates the user's local profile choice.</summary>
public interface IProfileService
{
    /// <summary>The chosen profile, or null on installs that have not chosen one yet.</summary>
    UserProfile? Current { get; }

    /// <summary>Raised after the profile has been saved.</summary>
    event EventHandler<UserProfile>? ProfileChanged;

    /// <summary>Persists the choice and raises <see cref="ProfileChanged"/>.</summary>
    Task SetProfileAsync(UserProfile profile);

    /// <summary>True when the current profile can see the feature.</summary>
    bool IsFeatureVisible(FeatureKey feature);
}

/// <summary>
/// <see cref="IProfileService"/> backed by the existing settings store. No account, no network,
/// no telemetry: the choice is one local setting.
/// </summary>
public sealed class ProfileService : IProfileService
{
    private readonly IAppSettingsService _settings;

    /// <summary>Creates the service over the shared settings service.</summary>
    public ProfileService(IAppSettingsService settings) =>
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <inheritdoc />
    public UserProfile? Current => _settings.Current.Profile;

    /// <inheritdoc />
    public event EventHandler<UserProfile>? ProfileChanged;

    /// <inheritdoc />
    public bool IsFeatureVisible(FeatureKey feature) =>
        ProfileFeatureMatrix.IsVisible(Current, feature);

    /// <inheritdoc />
    public async Task SetProfileAsync(UserProfile profile)
    {
        var current = _settings.Current;
        if (current.Profile == profile)
        {
            return;
        }

        current.Profile = profile;
        await _settings.SaveAsync(current).ConfigureAwait(false);
        ProfileChanged?.Invoke(this, profile);
    }
}
