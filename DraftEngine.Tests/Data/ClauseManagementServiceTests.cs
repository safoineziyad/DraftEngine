using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for <see cref="ClauseManagementService.ValidatePlaceholdersAsync" />:
/// unknown <c>{{Placeholder}}</c> keys must be detected and reported.
/// </summary>
public class ClauseManagementServiceTests
{
    private readonly TestDatabaseContextFactory _factory;

    public ClauseManagementServiceTests()
    {
        var options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _factory = new TestDatabaseContextFactory(options);
    }

    [Fact]
    public async Task ValidatePlaceholdersAsync_UnknownPlaceholder_IsDetected()
    {
        var service = new ClauseManagementService(_factory);
        var (isValid, unknown) = await service.ValidatePlaceholdersAsync("Hello {{Party1}}, meet {{MysteryGuest}}.");

        Assert.False(isValid);
        Assert.Contains("MysteryGuest", unknown);
    }

    [Fact]
    public async Task ValidatePlaceholdersAsync_AllKnown_IsValid()
    {
        var service = new ClauseManagementService(_factory);
        var (isValid, unknown) = await service.ValidatePlaceholdersAsync("{{Party1}} and {{Party2}}.");

        Assert.True(isValid);
        Assert.Empty(unknown);
    }

    [Fact]
    public async Task ValidatePlaceholdersAsync_Empty_IsValid()
    {
        var service = new ClauseManagementService(_factory);
        var (isValid, unknown) = await service.ValidatePlaceholdersAsync(string.Empty);

        Assert.True(isValid);
        Assert.Empty(unknown);
    }
}
