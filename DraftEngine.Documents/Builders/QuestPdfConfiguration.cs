using QuestPDF.Infrastructure;

namespace DraftEngine.Documents.Builders;

/// <summary>
/// Applies the process-wide QuestPDF settings exactly once, lazily and thread-safely.
/// QuestPDF requires its license to be configured before the first document is generated;
/// system fonts are re-enabled because DraftEngine is Windows-only and the Georgia body font
/// is a Windows system font (disabled by default since QuestPDF 2026.9).
/// </summary>
internal static class QuestPdfConfiguration
{
    private static readonly Lazy<bool> Initialized = new(
        Initialize,
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Ensures the QuestPDF license/font settings have been applied. Safe to call from any
    /// thread and from every export; only the first call performs the work.
    /// </summary>
    internal static void EnsureInitialized() => _ = Initialized.Value;

    private static bool Initialize()
    {
        // Community license is intentional: DraftEngine is a free, non-commercial project.
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;
        return true;
    }
}
