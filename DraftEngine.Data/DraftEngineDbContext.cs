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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureRelationships(modelBuilder);
        SeedClauses(modelBuilder);
    }
}
