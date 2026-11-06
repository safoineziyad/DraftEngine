using System;
using System.IO;
using System.Threading.Tasks;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.Core.Services;

/// <summary>
/// Persists encrypted wizard drafts on disk under the app-data directory so the
/// wizard can survive navigation and restarts.
/// </summary>
public class WizardDraftService : IWizardDraftService
{
    private static readonly string DraftDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DraftEngine",
        "wizard-drafts");

    public Task<string> LoadDraftAsync(string draftKey)
    {
        var path = GetDraftPath(draftKey);
        if (!File.Exists(path))
        {
            return Task.FromResult(string.Empty);
        }

        var payload = File.ReadAllText(path);
        return Task.FromResult(payload);
    }

    public Task SaveDraftAsync(string draftKey, string encryptedPayload)
    {
        Directory.CreateDirectory(DraftDirectory);
        var path = GetDraftPath(draftKey);
        File.WriteAllText(path, encryptedPayload ?? string.Empty);
        return Task.CompletedTask;
    }

    public Task DeleteDraftAsync(string draftKey)
    {
        var path = GetDraftPath(draftKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static string GetDraftPath(string draftKey)
    {
        var safeKey = string.IsNullOrWhiteSpace(draftKey) ? "draft" : draftKey.Trim();
        safeKey = safeKey.Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_')
            .Replace(' ', '_');

        return Path.Combine(DraftDirectory, safeKey + ".json");
    }
}
