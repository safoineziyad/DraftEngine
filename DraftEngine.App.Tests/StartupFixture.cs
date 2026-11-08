using System.IO;
using System.Xml.Linq;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services;
using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DraftEngine.App.Tests;

/// <summary>
/// Builds the real application service graph against an isolated temporary SQLite database.
/// Nothing in these tests touches the user's %APPDATA% database or makes a network call.
/// </summary>
public sealed class StartupFixture : IDisposable
{
    public StartupFixture()
    {
        Directory = Path.Combine(Path.GetTempPath(), "draftengine-app-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);

        Options = new AppOptions();
        Options.Data.ConnectionString = $"Data Source={Path.Combine(Directory, "test.db")}";
        // Keep every optional/commercial feature off, exactly as a default installation.
        Options.Features.LicensingEnabled = false;
        Options.Features.UpdateCheckEnabled = false;
        Options.Features.OnboardingEnabled = false;
        Options.Features.AboutEnabled = true;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDraftEngine(Options);
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public string Directory { get; }

    public AppOptions Options { get; }

    public ServiceProvider Provider { get; }

    public DraftEngineDbContext CreateContext()
    {
        var factory = Provider.GetRequiredService<IDbContextFactory<DraftEngineDbContext>>();
        return factory.CreateDbContext();
    }

    /// <summary>Adds one fully populated contract and returns its id.</summary>
    public int SeedContract()
    {
        using var context = CreateContext();
        var contract = new DraftEngine.Core.Models.Contract
        {
            UserId = 1,
            Title = "Acme Mutual NDA",
            ContractType = "MutualNda",
            Status = DraftEngine.Core.Enums.ContractStatus.Draft,
            ClientName = "Acme Holdings Inc.",
            PartiesJson = "[\"Acme Holdings Inc.\",\"Beta LLC\"]",
            GoverningLaw = "California",
            TermMonths = 24,
            EffectiveDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AssembledText =
                "Acme Mutual NDA\n\nThis Mutual Non-Disclosure Agreement is entered into by Acme Holdings Inc. and Beta LLC.\n\n" +
                "1. Confidentiality\nEach party shall protect the other party's confidential information.\n\n" +
                "2. Term\nThis agreement remains in effect for two years.\n",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Contracts.Add(contract);
        context.SaveChanges();
        return contract.Id;
    }

    /// <summary>Loads the linked App.xaml source markup copied beside the test assembly.</summary>
    public static XDocument LoadAppXaml()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "App.xaml");
        Assert.True(File.Exists(path), $"Expected linked App.xaml at '{path}'.");
        return XDocument.Load(path);
    }

    public void Dispose()
    {
        Provider.Dispose();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A file handle from SQLite may outlive the provider briefly; the temp folder is
            // disposable and the OS will clean it up.
        }
    }
}
