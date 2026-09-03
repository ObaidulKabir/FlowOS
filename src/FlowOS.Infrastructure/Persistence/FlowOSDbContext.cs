using FlowOS.Application.ReadModels;
using FlowOS.Domain.Entities;
using FlowOS.Events.Models; // Re-add this
using FlowOS.Workflows.Domain; // Re-add this
using FlowOS.Security.Models;
using Microsoft.EntityFrameworkCore;
using FlowOS.Notifications.Domain;

namespace FlowOS.Infrastructure.Persistence;

public class FlowOSDbContext : DbContext
{
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantApiKey> TenantApiKeys { get; set; }
    public DbSet<DomainEvent> Events { get; set; }
    public DbSet<WorkflowInstance> WorkflowInstances { get; set; }
    public DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; }
    public DbSet<StateMachineDefinition> StateMachineDefinitions { get; set; }
    public DbSet<EventDefinition> EventDefinitions { get; set; }
    public DbSet<AgentInsightReadModel> AgentInsights { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<Policy> Policies { get; set; }
    public DbSet<WorkflowClass> WorkflowClasses { get; set; } // Added WorkflowClass
    public DbSet<Notification> Notifications { get; set; } // Add this
    public DbSet<FlowOS.Core.Common.Models.OutboxMessage> OutboxMessages { get; set; }
    public DbSet<FlowOS.Core.Common.Models.WorkflowTimerJob> WorkflowTimerJobs { get; set; }

    public FlowOSDbContext(DbContextOptions<FlowOSDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowOSDbContext).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowOS.Notifications.Domain.Notification).Assembly); // Apply Notifications config
    }
}
