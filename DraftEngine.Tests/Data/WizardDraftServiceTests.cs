using DraftEngine.Core.Services;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Verifies the persisted wizard drafts survive a save/load cycle and can be removed
/// when the draft is discarded.
/// </summary>
public class WizardDraftServiceTests
{
    [Fact]
    public async Task SaveAndLoadDraftAsync_RoundTripsEncryptedPayload()
    {
        var service = new WizardDraftService();
        var key = "wizard-test-" + Guid.NewGuid().ToString("N");
        var payload = "encrypted-payload-value";

        await service.SaveDraftAsync(key, payload);

        try
        {
            var loaded = await service.LoadDraftAsync(key);
            Assert.Equal(payload, loaded);
        }
        finally
        {
            await service.DeleteDraftAsync(key);
        }
    }

    [Fact]
    public async Task DeleteDraftAsync_RemovesPersistedDraft()
    {
        var service = new WizardDraftService();
        var key = "wizard-delete-" + Guid.NewGuid().ToString("N");

        await service.SaveDraftAsync(key, "payload");
        await service.DeleteDraftAsync(key);

        var loaded = await service.LoadDraftAsync(key);
        Assert.Equal(string.Empty, loaded);
    }
}
