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
    public async Task Restore_KeepsEachWorkflowInItsApplication_AndReusesASameNamedApplication()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        await using var source = CreateContext();
        await using var destination = CreateContext();

        var tenant = new Tenant("Source Org");
        Set(tenant, "TenantId", sourceId);
        source.Tenants.Add(tenant);
        var finance = new TenantApplication(sourceId, "Finance Approvals", null, DateTime.UtcNow);
        var commerce = new TenantApplication(sourceId, "Commerce", "Orders", DateTime.UtcNow);
        var expense = new WorkflowClass(sourceId, "Expense", "1.0.0", new WorkflowClassBlueprint());
        expense.AssignToApplication(finance.Id);
        var order = new WorkflowClass(sourceId, "Order", "1.0.0", new WorkflowClassBlueprint());
        order.AssignToApplication(commerce.Id);
        source.TenantApplications.AddRange(finance, commerce);
        source.WorkflowClasses.AddRange(expense, order);
        await source.SaveChangesAsync();

        var exported = await new TenantBackupService(source).ExportJsonAsync(sourceId);

        var other = new Tenant("Destination Org");
        Set(other, "TenantId", destinationId);
        destination.Tenants.Add(other);
        var existingFinance = new TenantApplication(destinationId, "finance approvals", null, DateTime.UtcNow);
        destination.TenantApplications.Add(existingFinance);
        await destination.SaveChangesAsync();

        await new TenantBackupService(destination).RestoreJsonAsync(destinationId, exported);

        var applications = await destination.TenantApplications.Where(a => a.TenantId == destinationId).ToListAsync();
        Assert.Equal(2, applications.Count);
        var restoredCommerce = Assert.Single(applications, a => a.Name == "Commerce");
        Assert.Equal(commerce.Id, restoredCommerce.Id);
        Assert.Equal(existingFinance.Id, (await destination.WorkflowClasses.SingleAsync(w => w.Name == "Expense")).ApplicationId);
        Assert.Equal(commerce.Id, (await destination.WorkflowClasses.SingleAsync(w => w.Name == "Order")).ApplicationId);
    }

    [Fact]
    public async Task Restore_RePointsAnApplicationScopedPromptAtTheSameNamedApplication()
    {
        // Applications (3/4): a prompt/profile binding scoped to an application is re-pointed the same
        // way AssignToApplication re-points a workflow class on restore (see the test above).
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        await using var source = CreateContext();
        await using var destination = CreateContext();

        var tenant = new Tenant("Source Org");
        Set(tenant, "TenantId", sourceId);
        source.Tenants.Add(tenant);
        var finance = new TenantApplication(sourceId, "Finance Approvals", null, DateTime.UtcNow);
        source.TenantApplications.Add(finance);
        var sharedPrompt = new PluginBindingRecord(sourceId, "prompt", "quote-approval", "markdown");
        sharedPrompt.Update("markdown", true, "{\"instructions\":\"Shared.\"}");
        var scopedPrompt = new PluginBindingRecord(sourceId, "prompt", "quote-approval", "markdown", applicationId: finance.Id);
        scopedPrompt.Update("markdown", true, "{\"instructions\":\"App-specific.\"}");
        source.PluginBindings.AddRange(sharedPrompt, scopedPrompt);
        await source.SaveChangesAsync();

        var exported = await new TenantBackupService(source).ExportJsonAsync(sourceId);

        var other = new Tenant("Destination Org");
        Set(other, "TenantId", destinationId);
        destination.Tenants.Add(other);
        var existingFinance = new TenantApplication(destinationId, "finance approvals", null, DateTime.UtcNow);
        destination.TenantApplications.Add(existingFinance);
        await destination.SaveChangesAsync();

        var restored = await new TenantBackupService(destination).RestoreJsonAsync(destinationId, exported);
        Assert.Equal(2, restored.PluginBindings);

        var bindings = await destination.PluginBindings.Where(b => b.TenantId == destinationId).ToListAsync();
        var restoredShared = Assert.Single(bindings, b => b.ApplicationId == null);
        Assert.Equal("Shared.", AgentPromptConfiguration.Parse(restoredShared.ConfigurationJson)!.Body);
        var restoredScoped = Assert.Single(bindings, b => b.ApplicationId != null);
        Assert.Equal(existingFinance.Id, restoredScoped.ApplicationId);
        Assert.Equal("App-specific.", AgentPromptConfiguration.Parse(restoredScoped.ConfigurationJson)!.Body);
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
