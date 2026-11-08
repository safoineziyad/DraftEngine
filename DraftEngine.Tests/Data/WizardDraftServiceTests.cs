using System.Collections.Concurrent;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Models;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DraftEngine.Tests.Data;

/// <summary>Verifies wizard draft encryption and persistence in the SQLite database.</summary>
public class WizardDraftServiceTests
{
    [Fact]
    public async Task SaveAndLoadDraftAsync_EncryptsDatabasePayloadAndDeleteClearsIt()
    {
        await using var connection = await CreateOpenConnectionAsync();
        var factory = CreateFactory(connection);
        var protector = new TestSecretProtector();
        var service = new WizardDraftService(factory, protector);
        const string key = "wizard-test";
        const string payload = """{"ClientName":"Private Client Name","Purpose":"Private project"}""";

        await service.SaveDraftAsync(key, payload);
        Assert.Equal(payload, await service.LoadDraftAsync(key));

        await using (var context = factory.CreateDbContext())
        {
            var stored = await context.WizardDrafts.AsNoTracking().SingleAsync(draft => draft.DraftKey == key);
            Assert.NotEqual(payload, stored.EncryptedPayload);
            Assert.DoesNotContain("Private Client Name", stored.EncryptedPayload);
            Assert.DoesNotContain("Private project", stored.EncryptedPayload);
            Assert.Equal(protector.Protect(payload), stored.EncryptedPayload);
        }

        await service.DeleteDraftAsync(key);
        Assert.Equal(string.Empty, await service.LoadDraftAsync(key));
    }

    [Fact]
    public async Task SaveAndLoadDraftAsync_DoesNotWriteDraftContentToCapturedLogs()
    {
        await using var connection = await CreateOpenConnectionAsync();
        var service = new WizardDraftService(CreateFactory(connection), new TestSecretProtector());
        var sink = new CapturingSink();
        var originalLogger = Log.Logger;
        const string payload = """{"ClientName":"Never log this draft"}""";
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        try
        {
            await service.SaveDraftAsync("wizard-logging-test", payload);
            Assert.Equal(payload, await service.LoadDraftAsync("wizard-logging-test"));
            Assert.DoesNotContain(sink.Messages, message => message.Contains("Never log this draft", StringComparison.Ordinal));
            Assert.DoesNotContain(sink.Messages, message => message.Contains(payload, StringComparison.Ordinal));
        }
        finally
        {
            Log.Logger = originalLogger;
        }
    }

    private static async Task<SqliteConnection> CreateOpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new DraftEngineDbContext(
            new DbContextOptionsBuilder<DraftEngineDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        return connection;
    }

    private static TestDbContextFactory CreateFactory(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(connection)
            .Options;
        return new TestDbContextFactory(options);
    }

    private sealed class TestDbContextFactory(DbContextOptions<DraftEngineDbContext> options)
        : IDbContextFactory<DraftEngineDbContext>
    {
        public DraftEngineDbContext CreateDbContext() => new(options);

        public Task<DraftEngineDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class TestSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) =>
            "test-cipher:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

        public string? Unprotect(string protectedValue)
        {
            const string prefix = "test-cipher:";
            return protectedValue.StartsWith(prefix, StringComparison.Ordinal)
                ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue[prefix.Length..]))
                : null;
        }
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentBag<string> Messages { get; } = new();

        public void Emit(LogEvent logEvent) => Messages.Add(logEvent.RenderMessage());
    }
}
