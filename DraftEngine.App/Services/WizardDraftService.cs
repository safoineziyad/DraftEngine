using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.App.Services;

/// <summary>Stores DPAPI-protected wizard payloads in the application's SQLite database.</summary>
public sealed class WizardDraftService : IWizardDraftService
{
    private readonly IDbContextFactory<DraftEngineDbContext> _contextFactory;
    private readonly ISecretProtector _protector;

    public WizardDraftService(
        IDbContextFactory<DraftEngineDbContext> contextFactory,
        ISecretProtector protector)
    {
        _contextFactory = contextFactory;
        _protector = protector;
    }

    public async Task<string> LoadDraftAsync(string draftKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftKey);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var storedPayload = await context.WizardDrafts
            .AsNoTracking()
            .Where(draft => draft.DraftKey == draftKey)
            .Select(draft => draft.EncryptedPayload)
            .SingleOrDefaultAsync();

        return storedPayload is null ? string.Empty : _protector.Unprotect(storedPayload) ?? string.Empty;
    }

    public async Task SaveDraftAsync(string draftKey, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftKey);
        ArgumentNullException.ThrowIfNull(payload);

        var encryptedPayload = _protector.Protect(payload);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var draft = await context.WizardDrafts.FindAsync(draftKey);
        if (draft is null)
        {
            context.WizardDrafts.Add(new WizardDraft
            {
                DraftKey = draftKey,
                EncryptedPayload = encryptedPayload,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            draft.EncryptedPayload = encryptedPayload;
            draft.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();
    }

    public async Task DeleteDraftAsync(string draftKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftKey);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var draft = await context.WizardDrafts.FindAsync(draftKey);
        if (draft is null)
        {
            return;
        }

        context.WizardDrafts.Remove(draft);
        await context.SaveChangesAsync();
    }
}
