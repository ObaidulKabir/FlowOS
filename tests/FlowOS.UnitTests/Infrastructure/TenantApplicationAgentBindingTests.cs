using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Infrastructure;

/// <summary>
/// Applications (3/4): a prompt or agent profile binding may belong to a tenant application. These tests
/// cover the registry's application-scoped upsert/resolve/delete and the application delete block; the MCP
/// surface (ReadApplicationId/FilterByApplication) is covered by AgentContextMcpToolsTests.
/// </summary>
public class TenantApplicationAgentBindingTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static FlowOSDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"tenant-app-bindings-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task UpsertScoped_SavesInTheApplication_AndResolveForApplication_PrefersIt()
    {
        await using var db = NewDb();
        var apps = new TenantApplicationService(db);
        var bindings = new PluginBindingRegistryService(db);
        var app = await apps.CreateAsync(TenantId, "Finance Approvals", null);

        // Tenant-shared prompt with the same alias.
        await bindings.UpsertAsync(TenantId, PluginBindingTypes.Prompt, "quote-approval", "markdown",
            configurationJson: """{"title":"Shared","instructions":"Shared body."}""");

        // The application's own prompt with the same alias.
        await bindings.UpsertScopedAsync(TenantId, app.Id, PluginBindingTypes.Prompt, "quote-approval", "markdown",
            configurationJson: """{"title":"App-specific","instructions":"App body."}""");

        var resolved = await bindings.ResolveForApplicationAsync(TenantId, app.Id, PluginBindingTypes.Prompt, "quote-approval");
        var prompt = Assert.IsType<AgentPromptConfiguration>(resolved!.Configuration);
        Assert.Equal("App-specific", prompt.Title);
        Assert.Equal(app.Id, resolved.ApplicationId);

        // A different application with no prompt of its own falls back to the tenant-shared one.
        var otherApp = await apps.CreateAsync(TenantId, "Other App", null);
        var fallback = await bindings.ResolveForApplicationAsync(TenantId, otherApp.Id, PluginBindingTypes.Prompt, "quote-approval");
        var fallbackPrompt = Assert.IsType<AgentPromptConfiguration>(fallback!.Configuration);
        Assert.Equal("Shared", fallbackPrompt.Title);
        Assert.Null(fallback.ApplicationId);

        // Without an applicationId, only the tenant-shared binding resolves.
        var tenantOnly = await bindings.ResolveForApplicationAsync(TenantId, null, PluginBindingTypes.Prompt, "quote-approval");
        Assert.Null(tenantOnly!.ApplicationId);
    }

    [Fact]
    public async Task UpsertScoped_AgentProfile_ResolveAgentProfileForApplication_PrefersTheApplicationsOwnProfile()
    {
        await using var db = NewDb();
        var apps = new TenantApplicationService(db);
        var bindings = new PluginBindingRegistryService(db);
        var app = await apps.CreateAsync(TenantId, "Finance Approvals", null);

        await bindings.UpsertAsync(TenantId, PluginBindingTypes.Profile, "CreditUnderwriter", "LoanOfficer");
        await bindings.UpsertScopedAsync(TenantId, app.Id, PluginBindingTypes.Profile, "CreditUnderwriter", "LoanOfficer");

        var byAlias = await bindings.ResolveAgentProfileForApplicationAsync(TenantId, app.Id, "CreditUnderwriter");
        Assert.Equal(app.Id, byAlias!.ApplicationId);

        var byRole = await bindings.ResolveAgentProfileForApplicationAsync(TenantId, app.Id, "LoanOfficer");
        Assert.Equal(app.Id, byRole!.ApplicationId);
    }

    [Fact]
    public async Task UpsertScoped_RejectsNonPromptOrProfileBindingTypes()
    {
        await using var db = NewDb();
        var apps = new TenantApplicationService(db);
        var bindings = new PluginBindingRegistryService(db);
        var app = await apps.CreateAsync(TenantId, "Finance Approvals", null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            bindings.UpsertScopedAsync(TenantId, app.Id, PluginBindingTypes.Agent, "quote-llm", "openai"));
    }

    [Fact]
    public async Task UpsertScoped_RejectsAnApplicationNotInThisTenant()
    {
        await using var db = NewDb();
        var apps = new TenantApplicationService(db);
        var bindings = new PluginBindingRegistryService(db);
        var otherTenantApp = await apps.CreateAsync(Guid.NewGuid(), "Theirs", null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            bindings.UpsertScopedAsync(TenantId, otherTenantApp.Id, PluginBindingTypes.Prompt, "quote-approval", "markdown"));
    }

    [Fact]
    public async Task UpsertScoped_SameAliasAllowedOnceSharedAndOncePerApplication()
    {
        // The same alias is unique among tenant-shared bindings and separately within each application,
        // so an application may define its own prompt with the same alias as a shared one, and two
        // applications may each define their own with that alias too.
        await using var db = NewDb();
        var apps = new TenantApplicationService(db);
        var bindings = new PluginBindingRegistryService(db);
        var appA = await apps.CreateAsync(TenantId, "App A", null);
        var appB = await apps.CreateAsync(TenantId, "App B", null);

        await bindings.UpsertAsync(TenantId, PluginBindingTypes.Prompt, "quote-approval", "markdown");
        await bindings.UpsertScopedAsync(TenantId, appA.Id, PluginBindingTypes.Prompt, "quote-approval", "markdown");
        await bindings.UpsertScopedAsync(TenantId, appB.Id, PluginBindingTypes.Prompt, "quote-approval", "markdown");

        var all = await bindings.ListAsync(TenantId, PluginBindingTypes.Prompt, "quote-approval");
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task DeleteScoped_RemovesOnlyTheNamedScope()
    {
        await using var db = NewDb();
        var apps = new TenantApplicationService(db);
        var bindings = new PluginBindingRegistryService(db);
        var app = await apps.CreateAsync(TenantId, "Finance Approvals", null);

        await bindings.UpsertAsync(TenantId, PluginBindingTypes.Prompt, "quote-approval", "markdown");
        await bindings.UpsertScopedAsync(TenantId, app.Id, PluginBindingTypes.Prompt, "quote-approval", "markdown");

        var deletedAppScope = await bindings.DeleteScopedAsync(TenantId, app.Id, PluginBindingTypes.Prompt, "quote-approval");
        Assert.True(deletedAppScope);

        var remaining = await bindings.ListAsync(TenantId, PluginBindingTypes.Prompt, "quote-approval");
        var left = Assert.Single(remaining);
        Assert.Null(left.ApplicationId);

        var deleteAgain = await bindings.DeleteScopedAsync(TenantId, app.Id, PluginBindingTypes.Prompt, "quote-approval");
        Assert.False(deleteAgain);
    }

    [Fact]
    public async Task ApplicationDelete_IsRefused_WhileItHasAPromptOrProfileBinding()
    {
        await using var db = NewDb();
        var apps = new TenantApplicationService(db);
        var bindings = new PluginBindingRegistryService(db);
        var app = await apps.CreateAsync(TenantId, "Finance Approvals", null);
        await bindings.UpsertScopedAsync(TenantId, app.Id, PluginBindingTypes.Profile, "CreditUnderwriter", "LoanOfficer");

        var ex = await Assert.ThrowsAsync<TenantApplicationException>(() => apps.DeleteAsync(TenantId, app.Id));
        Assert.Equal(TenantApplicationErrorCodes.InUse, ex.Code);
        Assert.Contains("binding", ex.Message);

        await bindings.DeleteScopedAsync(TenantId, app.Id, PluginBindingTypes.Profile, "CreditUnderwriter");
        await apps.DeleteAsync(TenantId, app.Id);
        Assert.Empty(await apps.ListAsync(TenantId));
    }
}
