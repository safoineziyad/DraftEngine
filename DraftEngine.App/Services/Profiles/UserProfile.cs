using System.Text.Json.Serialization;

namespace DraftEngine.App.Services.Profiles;

/// <summary>
/// How the user intends to use DraftEngine. This is a local preference, never an account and
/// never sent anywhere. Existing installs have no profile and keep full (lawyer) behaviour.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserProfile
{
    /// <summary>Full drafting toolkit; every page is visible.</summary>
    Lawyer,

    /// <summary>Business use: general information and drafting, without clause-library tools.</summary>
    Business,

    /// <summary>Mediation use: the shared drafting pages plus a planned mediation workspace.</summary>
    Mediator,
}

/// <summary>Stable keys for the pages/features that already exist in the app.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeatureKey
{
    Dashboard,
    NewContract,
    Contracts,
    Archive,
    Analyze,
    Clauses,
    Settings,
    About,
}
