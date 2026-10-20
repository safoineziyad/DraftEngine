using DraftEngine.App.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace DraftEngine.App.Services;

/// <summary>
/// Registers the secure settings services (DPAPI secret protection + settings storage).
/// Called from <c>ServiceRegistration.AddDraftEngine</c>.
/// </summary>
public static class SettingsRegistration
{
    /// <summary>
    /// Adds <see cref="ISecretProtector"/> (DPAPI, current Windows user) and
    /// <see cref="IAppSettingsService"/> (settings.json under %APPDATA%\DraftEngine) as singletons.
    /// </summary>
    public static IServiceCollection AddAppSettings(this IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        return services;
    }
}
