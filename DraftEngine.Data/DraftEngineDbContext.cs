using System;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Data;

/// <summary>
/// Database context for DraftEngine.
/// </summary>
public partial class DraftEngineDbContext : DbContext
{
    public DraftEngineDbContext(DbContextOptions<DraftEngineDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Clause> Clauses => Set<Clause>();
    public DbSet<ContractClause> ContractClauses => Set<ContractClause>();
    public DbSet<ContractField> ContractFields => Set<ContractField>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<GeneratedDocument> GeneratedDocuments => Set<GeneratedDocument>();
    public DbSet<ContractVersion> ContractVersions => Set<ContractVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureRelationships(modelBuilder);
        SeedClauses(modelBuilder);
        SeedDefaultUser(modelBuilder);
    }

    /// <summary>
    /// Seeds the single local user account that owns every contract (A1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Contract.UserId"/> is a required foreign key to <c>Users.Id</c> with
    /// cascade delete. Nothing ever created that row, so every contract insert failed with
    /// "SQLite Error 19: FOREIGN KEY constraint failed" and the new-contract wizard could
    /// never save.
    /// </para>
    /// <para>
    /// This is a local desktop application with a single-operator model, so one fixed
    /// "local user" row is seeded through the migration instead of requiring a sign-up
    /// screen. The row is inserted by SQL (not through the interceptor), which is safe
    /// here because <see cref="User.OpenAiApiKeyEncrypted"/> is not one of the columns the
    /// <c>SensitiveDataInterceptor</c> encrypts.
    /// </para>
    /// </remarks>
    private static void SeedDefaultUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasData(new User
        {
            Id = 1,
            Email = "local@draftengine.local",
            DisplayName = "Local User",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            OpenAiApiKeyEncrypted = string.Empty,
            SubscriptionTier = "Free",
            SubscriptionExpiresAt = null
        });
    }
}
