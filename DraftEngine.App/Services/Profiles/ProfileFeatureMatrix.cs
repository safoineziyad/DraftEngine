using System;
using System.Collections.Generic;

namespace DraftEngine.App.Services.Profiles;

/// <summary>
/// The single source of truth for which existing pages each profile sees. Null (an install
/// created before profiles existed) behaves exactly like <see cref="UserProfile.Lawyer"/>.
/// Hiding a page never deletes its data; it only removes access to it.
/// </summary>
public static class ProfileFeatureMatrix
{
    private static readonly IReadOnlySet<FeatureKey> LawyerFeatures = new HashSet<FeatureKey>
    {
        FeatureKey.Dashboard,
        FeatureKey.NewContract,
        FeatureKey.Contracts,
        FeatureKey.Archive,
        FeatureKey.Analyze,
        FeatureKey.Clauses,
        FeatureKey.Settings,
        FeatureKey.About,
    };

    private static readonly IReadOnlySet<FeatureKey> BusinessFeatures = new HashSet<FeatureKey>
    {
        FeatureKey.Dashboard,
        FeatureKey.NewContract,
        FeatureKey.Contracts,
        FeatureKey.Archive,
        FeatureKey.Analyze,
        FeatureKey.Settings,
        FeatureKey.About,
    };

    // Mediator currently shares the business page set; the planned mediation workspace will
    // be added here when that page exists.
    private static readonly IReadOnlySet<FeatureKey> MediatorFeatures = new HashSet<FeatureKey>
    {
        FeatureKey.Dashboard,
        FeatureKey.NewContract,
        FeatureKey.Contracts,
        FeatureKey.Archive,
        FeatureKey.Analyze,
        FeatureKey.Settings,
        FeatureKey.About,
    };

    /// <summary>True when <paramref name="feature"/> is visible for the profile.</summary>
    public static bool IsVisible(UserProfile? profile, FeatureKey feature) =>
        FeaturesFor(profile).Contains(feature);

    /// <summary>The visible feature set for a profile (a copy; callers cannot mutate the matrix).</summary>
    public static IReadOnlySet<FeatureKey> FeaturesFor(UserProfile? profile) =>
        new HashSet<FeatureKey>(profile switch
        {
            UserProfile.Business => BusinessFeatures,
            UserProfile.Mediator => MediatorFeatures,
            _ => LawyerFeatures, // Lawyer and null (pre-profile installs)
        });

    /// <summary>True when the profile sees the Business "not legal advice" notice.</summary>
    public static bool ShowsNotLegalAdviceNotice(UserProfile? profile) =>
        profile == UserProfile.Business;

    /// <summary>True when the dashboard should mention the planned mediation workspace.</summary>
    public static bool ShowsMediationPlannedNote(UserProfile? profile) =>
        profile == UserProfile.Mediator;
}
