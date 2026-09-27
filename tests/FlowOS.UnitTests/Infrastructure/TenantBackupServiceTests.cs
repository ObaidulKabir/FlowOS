using FlowOS.Core.Common.Models;
using FlowOS.Domain;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Infrastructure;

public class TenantBackupServiceTests
{
    [Fact]
    public async Task Export_RoundTripsDesignedAppAndInstances_OntoAnotherSite()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        await using var source = CreateContext();
        await using var destination = CreateContext();

        var tenant = new Tenant("Source Org");
        Set(tenant, "TenantId", sourceId);
        source.Tenants.Add(tenant);
        source.TenantApiKeys.Add(new TenantApiKey(sourceId, "do-not-export-this-key", "flowos_secret_backup_key_32_chars"));

        var workflowClass = new WorkflowClass(sourceId, "Expense", "1.0.0", new WorkflowClassBlueprint());
        var definition = new WorkflowDefinition(sourceId, "Expense", 1, "Start");
        var instance = new WorkflowInstance(sourceId, definition.Id, workflowClass.Id, 1, "Start");
        var timer = new WorkflowTimerJob(sourceId, instance.Id, "Start", "SLA", DateTime.UtcNow.AddHours(1));
        var domainEvent = new StandardEvent(sourceId, "OrderCreated");
        domainEvent.AddMetadata("workflowInstanceId", instance.Id.ToString());
        var prompt = new PluginBindingRecord(sourceId, "prompt", "expense-review", "markdown");
        prompt.Update("markdown", true, "{\"instructions\":\"review the expense\"}");

        source.WorkflowClasses.Add(workflowClass);
        source.WorkflowDefinitions.Add(definition);
        source.WorkflowInstances.Add(instance);
        source.WorkflowTimerJobs.Add(timer);
        source.Events.Add(domainEvent);
        source.PluginBindings.Add(prompt);
        await source.SaveChangesAsync();

        var exported = await new TenantBackupService(source).ExportJsonAsync(sourceId);

        Assert.Contains(TenantBackupService.Format, exported);
        Assert.Contains(FlowOsRelease.Version, exported);
        Assert.Contains("Expense", exported);
        Assert.DoesNotContain("do-not-export-this-key", exported);
        Assert.Contains("review the expense", exported);

        var other = new Tenant("Destination Org");
        Set(other, "TenantId", destinationId);
        destination.Tenants.Add(other);
        await destination.SaveChangesAsync();

        var restored = await new TenantBackupService(destination).RestoreJsonAsync(destinationId, exported);

        Assert.Equal(sourceId, restored.SourceTenantId);
        Assert.Equal(destinationId, restored.DestinationTenantId);
        Assert.Equal(1, restored.WorkflowClasses);
        Assert.Equal(1, restored.WorkflowInstances);
        Assert.Equal(1, restored.Events);
        Assert.Equal(1, restored.PluginBindings);

        var moved = await destination.WorkflowClasses.SingleAsync();
        Assert.Equal(workflowClass.Id, moved.Id);
        Assert.Equal(destinationId, moved.TenantId);
        Assert.Equal(FlowOsRelease.Version, moved.FlowOsVersion);

        var movedInstance = await destination.WorkflowInstances.SingleAsync();
        Assert.Equal(instance.Id, movedInstance.Id);
        Assert.Equal("Start", movedInstance.CurrentStepId);
        Assert.Equal(destinationId, movedInstance.TenantId);

        var movedTimer = await destination.WorkflowTimerJobs.SingleAsync();
        Assert.True(movedTimer.IsProcessed);

        var conflict = await Assert.ThrowsAsync<TenantBackupException>(() =>
            new TenantBackupService(destination).RestoreJsonAsync(destinationId, exported));
        Assert.Equal("BACKUP-CONFLICT", conflict.Code);
    }

    [Fact]
    public async Task Restore_RefusesANewerFlowOsRelease()
    {
        var destinationId = Guid.NewGuid();
        await using var destination = CreateContext();
        var tenant = new Tenant("Destination Org");
        Set(tenant, "TenantId", destinationId);
        destination.Tenants.Add(tenant);
        await destination.SaveChangesAsync();

        var json = """
        {
          "format": "flowos-tenant-backup",
          "formatVersion": 1,
          "flowOsVersion": "2.0.0",
          "takenAtUtc": "2026-09-27T00:00:00Z",
          "sourceTenantId": "11111111-1111-1111-1111-111111111111",
          "sourceTenantName": "Elsewhere"
        }
        """;

        var ex = await Assert.ThrowsAsync<TenantBackupException>(() =>
            new TenantBackupService(destination).RestoreJsonAsync(destinationId, json));
        Assert.Equal("BACKUP-VERSION", ex.Code);
    }

    [Fact]
    public void ConfirmationHeader_MatchesOnlyTheExactAction()
    {
        Assert.True(TenantBackupConfirmation.IsConfirmed("download", TenantBackupConfirmation.Download));
        Assert.True(TenantBackupConfirmation.IsConfirmed(" restore ", TenantBackupConfirmation.Restore));
        Assert.False(TenantBackupConfirmation.IsConfirmed("download", TenantBackupConfirmation.Restore));
        Assert.False(TenantBackupConfirmation.IsConfirmed("Download", TenantBackupConfirmation.Download));
        Assert.False(TenantBackupConfirmation.IsConfirmed(null, TenantBackupConfirmation.Download));
    }

    private static FlowOSDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FlowOSDbContext(options);
    }

    private static void Set(object target, string propertyName, object value)
    {
        target.GetType().GetProperty(propertyName)!.SetValue(target, value);
    }
}
