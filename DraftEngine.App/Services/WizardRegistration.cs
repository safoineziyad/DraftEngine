using Microsoft.Extensions.DependencyInjection;

namespace DraftEngine.App.Services;

/// <summary>
/// DI registrations for the New Contract wizard. Called from <c>ServiceRegistration.AddDraftEngine</c>.
/// Page-level wizard view models are already registered there; the wizard's per-item
/// sub-view models (contract type cards, intake fields, clause rows, step indicators,
/// summary rows) are constructed directly by their parent view models and are not DI services.
/// </summary>
public static class WizardRegistration
{
    /// <summary>
    /// Adds the contract wizard support services (dialog helper) to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddContractWizard(this IServiceCollection services)
    {
        services.AddSingleton<IWizardDialogService, WizardDialogService>();
        return services;
    }
}
