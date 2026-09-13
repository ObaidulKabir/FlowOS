using System;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class PluginBindingRegistryServiceTests
{
    [Fact]
    public async Task UpsertAndResolve_ActionBinding_ShouldPersistAndResolve()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var service = new PluginBindingRegistryService(db);
        var tenantId = Guid.NewGuid();

        await service.UpsertAsync(
            tenantId,
            PluginBindingTypes.Action,
            sourceName: "plugin:paymentRefunder",
            providerName: "InvokeCapability");

        var resolved = await service.ResolveProviderNameAsync(
            tenantId,
            PluginBindingTypes.Action,
            "plugin:paymentRefunder");

        Assert.Equal("InvokeCapability", resolved);
    }

    [Fact]
    public async Task Upsert_DisabledBinding_ShouldNotResolve()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var service = new PluginBindingRegistryService(db);
        var tenantId = Guid.NewGuid();

        await service.UpsertAsync(
            tenantId,
            PluginBindingTypes.Decision,
            sourceName: "risk-default",
            providerName: "risk-v2",
            isEnabled: false);

        var resolved = await service.ResolveProviderNameAsync(
            tenantId,
            PluginBindingTypes.Decision,
            "risk-default");

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveBindings_ShouldReturnCaseInsensitiveMap()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var service = new PluginBindingRegistryService(db);
        var tenantId = Guid.NewGuid();

        await service.UpsertAsync(tenantId, PluginBindingTypes.Decision, "risk-default", "risk-v2");
        var map = await service.ResolveBindingsAsync(tenantId, PluginBindingTypes.Decision);

        Assert.True(map.TryGetValue("RISK-DEFAULT", out var mapped));
        Assert.Equal("risk-v2", mapped);
    }
}
