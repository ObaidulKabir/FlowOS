using System;
using System.Threading.Tasks;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace FlowOS.EndToEndTests.Concurrency;

/// <summary>
/// Shared Testcontainers-backed PostgreSQL fixture.
/// One container is started per test-class collection and torn down after all tests complete.
/// </summary>
public sealed class PostgresConcurrencyFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("flowos_concurrency_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Apply EF migrations to the live container
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseNpgsql(ConnectionString, b => b.MigrationsAssembly(typeof(FlowOSDbContext).Assembly.FullName))
            .Options;

        await using var ctx = new FlowOSDbContext(options);
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    public FlowOSDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new FlowOSDbContext(options);
    }
}
