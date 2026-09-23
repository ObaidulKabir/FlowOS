using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.Security.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FlowOS.UnitTests.Security;

public class ApiKeyEffectiveCapabilitiesTests
{
    [Fact]
    public async Task ScopedKey_IntersectsRoleCapabilities_WithScopeBoundary()
    {
        await using var context = CreateContext();
        var tenantId = Guid.NewGuid();
        var role = new Role(tenantId, "ApiKey");
        role.AddPermission("workflow.start");
        role.AddPermission("workflow.read");
        role.AddPermission("event.publish");
        context.Roles.Add(role);
        await context.SaveChangesAsync();

        var service = new CapabilityService(context, new MemoryCache(new MemoryCacheOptions()));

        var readOnly = await service.GetEffectiveCapabilitiesAsync(
            tenantId,
            new[] { "ApiKey" },
            new[] { "workflow:read" },
            isApiKey: true);
        var workflowOperator = await service.GetEffectiveCapabilitiesAsync(
            tenantId,
            new[] { "ApiKey" },
            new[] { "workflow:start", "event:publish" },
            isApiKey: true);

        Assert.Contains("workflow.read", readOnly);
        Assert.DoesNotContain("workflow.start", readOnly);
        Assert.Contains("workflow.start", workflowOperator);
        Assert.Contains("event.publish", workflowOperator);
        Assert.DoesNotContain("workflow.read", workflowOperator);
    }

    [Fact]
    public async Task WildcardKey_PreservesAllRoleCapabilities()
    {
        await using var context = CreateContext();
        var tenantId = Guid.NewGuid();
        var role = new Role(tenantId, "Admin");
        role.AddPermission("workflow.start");
        role.AddPermission("iam.manage");
        context.Roles.Add(role);
        await context.SaveChangesAsync();

        var service = new CapabilityService(context, new MemoryCache(new MemoryCacheOptions()));
        var effective = await service.GetEffectiveCapabilitiesAsync(
            tenantId,
            new[] { "Admin" },
            new[] { "*" },
            isApiKey: true);

        Assert.Contains("workflow.start", effective);
        Assert.Contains("iam.manage", effective);
    }

    private static FlowOSDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FlowOSDbContext(options);
    }
}
