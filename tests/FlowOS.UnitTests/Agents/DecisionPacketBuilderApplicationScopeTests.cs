using FlowOS.API.Services;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Agents;

/// <summary>
/// Applications (3/4): DecisionPacketBuilder resolves a step's agentPrompt/agentProvider in the
/// workflow's own application first, then among tenant-shared bindings - exercised here via
/// PreviewAsync against the seeded QuoteAutoReview class, using the real PluginBindingRegistryService
/// (unlike QuoteAgentContextBindingTests' fixed-prompt fixture, which cannot hold two bindings with the
/// same alias to tell application-scoped apart from tenant-shared).
/// </summary>
public class DecisionPacketBuilderApplicationScopeTests
{
    private static async Task<(Guid TenantId, FlowOSDbContext Db, FlowOS.Domain.Entities.WorkflowClass Pack)> SeedQuoteAsync()
    {
        var tenantId = Guid.NewGuid();
        var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"quote-agent-ctx-app-scope-{tenantId}")
            .Options);

        await DataSeeder.SeedFlagshipWorkflowsAsync(db, tenantId);
        var pack = await db.WorkflowClasses.SingleAsync(item =>
            item.TenantId == tenantId && item.Name == "QuoteAutoReview");
        return (tenantId, db, pack);
    }

    [Fact]
    public async Task Preview_WorkflowAssignedToApplication_PrefersTheApplicationsOwnPrompt()
    {
        var (tenantId, db, pack) = await SeedQuoteAsync();
        await using (db)
        {
            var apps = new TenantApplicationService(db);
            var bindings = new PluginBindingRegistryService(db);
            var app = await apps.CreateAsync(tenantId, "Quote Desk", null);

            await bindings.UpsertAsync(tenantId, PluginBindingTypes.Prompt, "quote-approval", "markdown",
                configurationJson: """{"title":"Shared","instructions":"Shared instructions."}""");
            await bindings.UpsertScopedAsync(tenantId, app.Id, PluginBindingTypes.Prompt, "quote-approval", "markdown",
                configurationJson: """{"title":"App-specific","instructions":"App-specific instructions."}""");

            pack.AssignToApplication(app.Id);
            await db.SaveChangesAsync();

            var builder = new DecisionPacketBuilder(new UnitOfWork(db), bindings);
            var packet = await builder.PreviewAsync(tenantId, pack.Id, "AgentReview");

            Assert.NotNull(packet);
            Assert.Equal("App-specific instructions.", packet!.Prompt.Instructions);
        }
    }

    [Fact]
    public async Task Preview_UnassignedWorkflow_FallsBackToTenantSharedPrompt()
    {
        var (tenantId, db, pack) = await SeedQuoteAsync();
        await using (db)
        {
            var apps = new TenantApplicationService(db);
            var bindings = new PluginBindingRegistryService(db);
            var app = await apps.CreateAsync(tenantId, "Quote Desk", null);

            await bindings.UpsertAsync(tenantId, PluginBindingTypes.Prompt, "quote-approval", "markdown",
                configurationJson: """{"title":"Shared","instructions":"Shared instructions."}""");
            await bindings.UpsertScopedAsync(tenantId, app.Id, PluginBindingTypes.Prompt, "quote-approval", "markdown",
                configurationJson: """{"title":"App-specific","instructions":"App-specific instructions."}""");

            // pack is never assigned to app - stays unassigned (ApplicationId == null).
            var builder = new DecisionPacketBuilder(new UnitOfWork(db), bindings);
            var packet = await builder.PreviewAsync(tenantId, pack.Id, "AgentReview");

            Assert.NotNull(packet);
            Assert.Equal("Shared instructions.", packet!.Prompt.Instructions);
        }
    }

    [Fact]
    public async Task Preview_ApplicationHasNoOwnPrompt_FallsBackToTenantSharedPrompt()
    {
        var (tenantId, db, pack) = await SeedQuoteAsync();
        await using (db)
        {
            var apps = new TenantApplicationService(db);
            var bindings = new PluginBindingRegistryService(db);
            var app = await apps.CreateAsync(tenantId, "Quote Desk", null);

            await bindings.UpsertAsync(tenantId, PluginBindingTypes.Prompt, "quote-approval", "markdown",
                configurationJson: """{"title":"Shared","instructions":"Shared instructions."}""");

            pack.AssignToApplication(app.Id);
            await db.SaveChangesAsync();

            var builder = new DecisionPacketBuilder(new UnitOfWork(db), bindings);
            var packet = await builder.PreviewAsync(tenantId, pack.Id, "AgentReview");

            Assert.NotNull(packet);
            Assert.Equal("Shared instructions.", packet!.Prompt.Instructions);
        }
    }
}
