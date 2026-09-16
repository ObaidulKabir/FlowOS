using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FlowOS.UnitTests.Infrastructure;

public class FlowOsDatabaseTests
{
    [Fact]
    public void Production_and_staging_use_postgres_even_if_in_memory_flag_is_set()
    {
        foreach (var environment in new[] { "Production", "Staging", "production", "" })
        {
            var options = new DbContextOptionsBuilder<FlowOSDbContext>();
            FlowOsDatabase.Configure(options, environment, Config(
                useInMemory: true,
                connection: "Host=postgres;Port=5432;Database=flowos;Username=root;Password=toor"));

            Assert.True(FlowOsDatabase.IsNpgsql(options));
            Assert.False(FlowOsDatabase.IsInMemory(options));
        }
    }

    [Fact]
    public void Postgres_connection_wins_in_development()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>();
        FlowOsDatabase.Configure(options, "Development", Config(
            useInMemory: true,
            connection: "Host=postgres;Port=5432;Database=flowos;Username=postgres;Password=password"));

        Assert.True(FlowOsDatabase.IsNpgsql(options));
        Assert.False(FlowOsDatabase.IsInMemory(options));
    }

    [Fact]
    public void In_memory_is_only_for_development_without_postgres()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>();
        FlowOsDatabase.Configure(options, "Development", Config(useInMemory: true, connection: "Host=;Database=flowos"));

        Assert.True(FlowOsDatabase.IsInMemory(options));
        Assert.False(FlowOsDatabase.IsNpgsql(options));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public void Live_environments_refuse_in_memory_without_postgres(string environment)
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>();
        var error = Assert.Throws<InvalidOperationException>(() =>
            FlowOsDatabase.Configure(options, environment, Config(useInMemory: true, connection: "Host=")));

        Assert.Contains("PostgreSQL", error.Message);
        Assert.Contains("Tenant API keys", error.Message);
    }

    private static IConfiguration Config(bool useInMemory, string connection) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UseInMemoryDatabase"] = useInMemory ? "true" : "false",
            ["ConnectionStrings:DefaultConnection"] = connection
        }).Build();
}
