using FlowOS.Domain.Entities;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence.ReadModels;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence;

public class FlowOSDbContext : DbContext
{
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<DomainEvent> Events { get; set; }
    public DbSet<WorkflowInstance> WorkflowInstances { get; set; }
    public DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; }
    public DbSet<AgentInsightReadModel> AgentInsights { get; set; }

    public FlowOSDbContext(DbContextOptions<FlowOSDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowOSDbContext).Assembly);
    }
}
