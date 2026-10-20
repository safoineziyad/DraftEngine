using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DraftEngine.Data;

/// <summary>
/// Design-time factory used by the EF Core tools (dotnet ef) to create the DbContext
/// without starting the WPF application.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DraftEngineDbContext>
{
    public DraftEngineDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DraftEngineDbContext>();
        optionsBuilder.UseSqlite("Data Source=draftengine.db");
        return new DraftEngineDbContext(optionsBuilder.Options);
    }
}
