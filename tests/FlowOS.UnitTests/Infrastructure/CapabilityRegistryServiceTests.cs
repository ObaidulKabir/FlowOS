using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class CapabilityRegistryServiceTests
{
    [Fact]
    public async Task UpsertAndList_ShouldPersistCapabilityBinding()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var svc = new CapabilityRegistryService(db);
        var tenantId = Guid.NewGuid();

        await svc.UpsertAsync(
            tenantId,
            capabilityName: "payment.refund.v1",
            endpointUrl: "https://worker.example.com/capabilities/refund",
            transport: "http",
            timeoutMs: 15000);

        var list = await svc.ListAsync(tenantId);
        Assert.Single(list);
        Assert.Equal("payment.refund.v1", list[0].CapabilityName);
        Assert.Equal("https://worker.example.com/capabilities/refund", list[0].EndpointUrl);
    }

    [Fact]
    public async Task ValidateBinding_ShouldReturnFalse_WhenDisabledOrInvalid()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var svc = new CapabilityRegistryService(db);
        var tenantId = Guid.NewGuid();

        await svc.UpsertAsync(
            tenantId,
            capabilityName: "shipping.quote.v1",
            endpointUrl: "not-a-url",
            transport: "http",
            isEnabled: false);

        var (isValid, message, binding) = await svc.ValidateBindingAsync(tenantId, "shipping.quote.v1");
        Assert.False(isValid);
        Assert.NotNull(binding);
        Assert.Contains("disabled", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upsert_ShouldUpdateExistingBinding()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var svc = new CapabilityRegistryService(db);
        var tenantId = Guid.NewGuid();

        await svc.UpsertAsync(tenantId, "risk.score.v1", "https://old.example.com", timeoutMs: 5000);
        await svc.UpsertAsync(tenantId, "risk.score.v1", "https://new.example.com", timeoutMs: 12000);

        var list = await svc.ListAsync(tenantId, "risk.score.v1");
        Assert.Single(list);
        Assert.Equal("https://new.example.com", list.First().EndpointUrl);
        Assert.Equal(12000, list.First().TimeoutMs);
    }
}
