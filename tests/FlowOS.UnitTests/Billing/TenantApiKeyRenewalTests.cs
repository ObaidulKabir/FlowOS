using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Billing;

public class TenantApiKeyRenewalTests
{
    [Fact]
    public void ExtendExpiry_MovesAnExpiryLater_ButNeverEarlier_AndSkipsRevokedAndPermanentKeys()
    {
        var tenantId = Guid.NewGuid();
        var soon = DateTime.UtcNow.AddDays(1);
        var expiring = new TenantApiKey(tenantId, "a", TenantApiKey.GenerateRawKey(), expiresAt: soon);
        var permanent = new TenantApiKey(tenantId, "b", TenantApiKey.GenerateRawKey(), expiresAt: null);
        var revoked = new TenantApiKey(tenantId, "c", TenantApiKey.GenerateRawKey(), expiresAt: soon);
        revoked.Revoke();

        Assert.True(expiring.ExtendExpiry(soon.AddDays(30)));
        Assert.Equal(soon.AddDays(30), expiring.ExpiresAt);
        Assert.False(expiring.ExtendExpiry(soon));
        Assert.False(permanent.ExtendExpiry(soon.AddDays(30)));
        Assert.Null(permanent.ExpiresAt);
        Assert.False(revoked.ExtendExpiry(soon.AddDays(30)));
        Assert.Equal(soon, revoked.ExpiresAt);
    }

    [Fact]
    public async Task ExtendExpiringKeys_RenewsLapsedKeys_OfThatTenantOnly()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new FlowOSDbContext(options);
        var tenant = new Tenant("Renewal Org");
        var other = new Tenant("Other Org");
        db.Tenants.AddRange(tenant, other);
        var lapsed = new TenantApiKey(tenant.TenantId, "lapsed", TenantApiKey.GenerateRawKey(), expiresAt: DateTime.UtcNow.AddDays(-3));
        var permanent = new TenantApiKey(tenant.TenantId, "permanent", TenantApiKey.GenerateRawKey(), expiresAt: null);
        var revoked = new TenantApiKey(tenant.TenantId, "revoked", TenantApiKey.GenerateRawKey(), expiresAt: DateTime.UtcNow.AddDays(-3));
        revoked.Revoke();
        var foreign = new TenantApiKey(other.TenantId, "foreign", TenantApiKey.GenerateRawKey(), expiresAt: DateTime.UtcNow.AddDays(-3));
        db.TenantApiKeys.AddRange(lapsed, permanent, revoked, foreign);
        await db.SaveChangesAsync();

        var service = new TenantApiKeyRenewalService(db, TimeProvider.System);
        var changed = await service.ExtendExpiringKeysAsync(tenant.TenantId, 30);

        Assert.Equal(1, changed);
        Assert.True(lapsed.IsActive);
        Assert.InRange(lapsed.ExpiresAt!.Value, DateTime.UtcNow.AddDays(29), DateTime.UtcNow.AddDays(31));
        Assert.Null(permanent.ExpiresAt);
        Assert.False(revoked.IsActive);
        Assert.True(foreign.IsExpired);
        Assert.Equal(0, await service.ExtendExpiringKeysAsync(tenant.TenantId, 0));
    }
}
