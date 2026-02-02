using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.EndToEndTests;

public class TestFlowOSDbContext : FlowOSDbContext
{
    public TestFlowOSDbContext(DbContextOptions<FlowOSDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Add any test-specific configurations here if needed
    }
}
