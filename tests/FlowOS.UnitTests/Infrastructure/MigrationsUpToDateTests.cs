using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Infrastructure;

/// <summary>
/// Fails when the EF Core model differs from the latest migration snapshot. After a package or model
/// change, add a migration (`dotnet ef migrations add <Name> -p src/FlowOS.Infrastructure -s src/FlowOS.Api`)
/// instead of letting production start with pending model changes. No database connection is needed.
/// </summary>
public class MigrationsUpToDateTests
{
    [Fact]
    public void Model_has_no_pending_changes_against_the_migration_snapshot()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseNpgsql("Host=localhost;Database=flowos_model_check;Username=unused;Password=unused")
            .Options;

        using var context = new FlowOSDbContext(options);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The EF Core model has changes that are not in a migration. Add a migration.");
    }
}
