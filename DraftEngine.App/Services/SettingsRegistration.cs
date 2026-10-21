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
    /// <see cref="IAppSettingsService"/> (settings.json under %APPDATA%\DraftEngine) as
    /// singletons, plus the shared "openai" <see cref="System.Net.Http.HttpClient"/> pipeline
    /// (audit Finding #6: no client is constructed by hand, so sockets are recycled properly).
    /// </summary>
    public static IServiceCollection AddAppSettings(this IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();

        // One named client for every OpenAI call. Each CreateClient() call still returns a
        // distinct HttpClient instance (per-service Bearer headers stay isolated) while the
        // underlying handlers are pooled and rotated by the factory.
        services.AddHttpClient("openai", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        return services;
    }
}
