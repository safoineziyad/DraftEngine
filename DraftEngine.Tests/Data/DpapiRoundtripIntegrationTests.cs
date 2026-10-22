using System.Security.Cryptography;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Real disk-level integration tests for Windows DPAPI column encryption (Finding #3).
/// These tests exercise the production interceptor against actual SQLite files on disk
/// and inspect raw bytes without EF to prove values are encrypted at rest.
/// </summary>
public class DpapiRoundtripIntegrationTests
{
    [Fact]
    public async Task Roundtrip_ViaSeparateContexts_DecryptedOnLoad()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"draftengine_dpapi_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .AddInterceptors(new SensitiveDataInterceptor())
                .Options;

            // Create schema and insert contract
            using (var context = new DraftEngineDbContext(options))
            {
                context.Database.EnsureCreated();
                context.Contracts.Add(new Contract
                {
                    User = new User { Email = "dev@example.com", DisplayName = "Dev" },
                    Title = "Mutual NDA — Acme",
                    ContractType = "Mutual NDA",
                    ClientName = "John Smith",
                    ClientEmail = "john@acme.example",
                    PartiesJson = """[{"name":"Acme"},{"name":"Globex"}]""",
                    AssembledText = "full contract text",
                    Status = ContractStatus.Generated,
                });
                await context.SaveChangesAsync();
            }

            // Load from a new context (must decrypt)
            using (var context = new DraftEngineDbContext(options))
            {
                var loaded = await context.Contracts.AsNoTracking().SingleAsync();
                Assert.Equal("John Smith", loaded.ClientName);
                Assert.Equal("full contract text", loaded.AssembledText);
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task DiskInspection_ValuesAreEncryptedWithMarker()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"draftengine_dpapi_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .AddInterceptors(new SensitiveDataInterceptor())
                .Options;

            using (var context = new DraftEngineDbContext(options))
            {
                context.Database.EnsureCreated();
                context.Contracts.Add(new Contract
                {
                    User = new User { Email = "dev@example.com", DisplayName = "Dev" },
                    Title = "Mutual NDA — Acme",
                    ContractType = "Mutual NDA",
                    ClientName = "John Smith",
                    ClientEmail = "john@acme.example",
                    PartiesJson = """[{"name":"Acme"}]""",
                    AssembledText = "full contract text",
                    Status = ContractStatus.Generated,
                });
                await context.SaveChangesAsync();
            }

            // Raw inspection with no EF/interceptor
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ClientName, ClientEmail, PartiesJson, AssembledText FROM Contracts LIMIT 1";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());

            var clientName = reader.GetString(0);
            var clientEmail = reader.GetString(1);
            var partiesJson = reader.GetString(2);
            var assembledText = reader.GetString(3);

            Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, clientName, StringComparison.Ordinal);
            Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, clientEmail, StringComparison.Ordinal);
            Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, partiesJson, StringComparison.Ordinal);
            Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, assembledText, StringComparison.Ordinal);

            Assert.DoesNotContain("John Smith", clientName);
            Assert.DoesNotContain("full contract text", assembledText);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task LegacyV01_PlaintextLoadsAndIsReEncryptedOnSave()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"draftengine_dpapi_{Guid.NewGuid():N}.db");
        try
        {
            // Create schema without interceptor, insert plaintext (v0.1 style)
            var plainOptions = new DbContextOptionsBuilder<DraftEngineDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using (var context = new DraftEngineDbContext(plainOptions))
            {
                context.Database.EnsureCreated();
                context.Contracts.Add(new Contract
                {
                    User = new User { Email = "dev@example.com", DisplayName = "Dev" },
                    Title = "Mutual NDA",
                    ContractType = "Mutual NDA",
                    ClientName = "Legacy Client",
                    ClientEmail = "legacy@example.com",
                    PartiesJson = "[]",
                    AssembledText = "legacy plaintext",
                    Status = ContractStatus.Generated,
                });
                await context.SaveChangesAsync();
            }

            // Load through interceptor-enabled context — legacy plaintext must load as-is
            var encryptedOptions = new DbContextOptionsBuilder<DraftEngineDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .AddInterceptors(new SensitiveDataInterceptor())
                .Options;

            using (var context = new DraftEngineDbContext(encryptedOptions))
            {
                var loaded = await context.Contracts.SingleAsync();
                Assert.Equal("Legacy Client", loaded.ClientName);
                Assert.Equal("legacy plaintext", loaded.AssembledText);

                // Touch a tracked property to trigger save path
                loaded.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }

            // After save, raw disk should be encrypted
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ClientName FROM Contracts LIMIT 1";
            var raw = (string)cmd.ExecuteScalar()!;
            Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, raw, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task DecryptFailure_ReturnsCiphertextWithoutCorruption()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"draftengine_dpapi_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .AddInterceptors(new SensitiveDataInterceptor())
                .Options;

            using (var context = new DraftEngineDbContext(options))
            {
                context.Database.EnsureCreated();
                context.Contracts.Add(new Contract
                {
                    User = new User { Email = "dev@example.com", DisplayName = "Dev" },
                    Title = "Test",
                    ContractType = "Mutual NDA",
                    ClientName = "Safe Client",
                    ClientEmail = "safe@example.com",
                    PartiesJson = """[{"name":"Acme"}]""",
                    AssembledText = "safe text",
                    Status = ContractStatus.Generated,
                });
                await context.SaveChangesAsync();
            }

            // Force a real decryption failure: the row now holds a marker-prefixed value whose
            // payload is not a valid DPAPI blob, which is what a row encrypted under a different
            // Windows profile (or a damaged blob) looks like. ProtectedData.Unprotect then throws
            // a CryptographicException inside the interceptor.
            // AssembledText is deliberately the LAST encrypted property of Contract
            // (ClientName, ClientEmail, PartiesJson, AssembledText): the interceptor decrypts the
            // properties in that order inside a single try/catch that aborts the loop at the
            // first failure, so corrupting the last one keeps the other three decryptable and
            // proves the failure was isolated to the corrupt column.
            var corruptValue = SensitiveDataInterceptor.MarkerPrefix + Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });

            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Contracts SET AssembledText = $value";
                cmd.Parameters.AddWithValue("$value", corruptValue);
                cmd.ExecuteNonQuery();
            }

            // Raw values on disk before the failing load.
            string rawClientNameBefore;
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT ClientName, AssembledText FROM Contracts LIMIT 1";
                using var reader = cmd.ExecuteReader();
                Assert.True(reader.Read());
                rawClientNameBefore = reader.GetString(0);
                Assert.Equal(corruptValue, reader.GetString(1));
            }

            // A failed decryption may never break materialization: the load must not throw.
            Contract loaded;
            using (var context = new DraftEngineDbContext(options))
            {
                loaded = await context.Contracts.AsNoTracking().SingleAsync();
            }

            // The intact columns are still decrypted: the failure was isolated to AssembledText.
            Assert.Equal("Safe Client", loaded.ClientName);
            Assert.Equal("safe@example.com", loaded.ClientEmail);
            Assert.Equal("""[{"name":"Acme"}]""", loaded.PartiesJson);

            // The corrupt column is returned as stored: marker and ciphertext untouched,
            // not empty, not null, not plaintext and not further mangled.
            Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, loaded.AssembledText, StringComparison.Ordinal);
            Assert.Equal(corruptValue, loaded.AssembledText);

            // The failed load did not write back: the row on disk is byte-for-byte what was written.
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT ClientName, AssembledText FROM Contracts LIMIT 1";
                using var reader = cmd.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal(rawClientNameBefore, reader.GetString(0));
                Assert.Equal(corruptValue, reader.GetString(1));
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var wal = path + "-wal";
            var shm = path + "-shm";
            if (File.Exists(wal)) File.Delete(wal);
            if (File.Exists(shm)) File.Delete(shm);
        }
        catch { /* best effort */ }
    }
}
