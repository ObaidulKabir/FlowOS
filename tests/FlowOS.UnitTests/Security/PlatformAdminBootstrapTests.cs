using FlowOS.API.Services;
using FlowOS.Core.Security;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Security;

public class PlatformAdminBootstrapTests
{
    private const string Email = "platform-admin@flowosbd.com";
    private const string Password = "platform-admin-password-value";
    private const string ApiKey = "flw_live_platform_admin_key_not_a_demo_value";

    [Fact]
    public async Task EnsurePlatformAdministrator_CreatesVerifiedAdminAndKey()
    {
        await using var db = NewDb();
        var hasher = new Pbkdf2PasswordHasher();

        await DataSeeder.EnsurePlatformAdministratorAsync(db, hasher, Email, Password, ApiKey);
        await DataSeeder.EnsurePlatformAdministratorAsync(db, hasher, "  " + Email.ToUpperInvariant(), Password, ApiKey);

        var user = await db.TenantUsers.SingleAsync();
        Assert.Equal(TenantIdentityRules.PlatformTenantId, user.TenantId);
        Assert.Equal("Admin", user.Role);
        Assert.True(user.IsEmailVerified);
        Assert.True(hasher.VerifyPassword(Password, user.PasswordHash));

        var key = await db.TenantApiKeys.SingleAsync();
        Assert.Equal(TenantIdentityRules.PlatformTenantId, key.TenantId);
        Assert.Equal(TenantApiKey.HashKey(ApiKey), key.KeyHash);
        Assert.Contains("*", key.Scopes);
        Assert.False(key.IsRevoked);
    }

    [Fact]
    public async Task EnsurePlatformAdministrator_IgnoresDemoKeyAndShortPassword()
    {
        await using var db = NewDb();
        var hasher = new Pbkdf2PasswordHasher();

        await DataSeeder.EnsurePlatformAdministratorAsync(db, hasher, Email, "short", ApiKey);
        await DataSeeder.EnsurePlatformAdministratorAsync(
            db, hasher, Email, Password, "flowos_prod_secret_key_32_chars_min");

        Assert.Empty(await db.TenantUsers.ToListAsync());
        Assert.Empty(await db.TenantApiKeys.ToListAsync());
    }

    [Fact]
    public async Task EnsurePlatformAdministrator_DoesNotAttachKeyToAnotherTenantsEmail()
    {
        await using var db = NewDb();
        var hasher = new Pbkdf2PasswordHasher();
        var otherTenant = Guid.Parse("33333333-3333-3333-3333-333333333333");
        db.Tenants.Add(new Tenant("Other"));
        db.TenantUsers.Add(new TenantUser(
            otherTenant, Email, hasher.HashPassword("other-password-value"), "Other", "Admin", true));
        await db.SaveChangesAsync();

        await DataSeeder.EnsurePlatformAdministratorAsync(db, hasher, Email, Password, ApiKey);

        Assert.Equal(otherTenant, (await db.TenantUsers.SingleAsync()).TenantId);
        Assert.Empty(await db.TenantApiKeys.ToListAsync());
    }

    [Fact]
    public async Task EnsurePlatformAdministrator_DoesNotCreateUserWhenKeyBelongsToAnotherTenant()
    {
        await using var db = NewDb();
        var hasher = new Pbkdf2PasswordHasher();
        var otherTenant = Guid.Parse("44444444-4444-4444-4444-444444444444");
        db.TenantApiKeys.Add(new TenantApiKey(otherTenant, "Other", ApiKey, "Other App", "Production", new[] { "*" }));
        await db.SaveChangesAsync();

        await DataSeeder.EnsurePlatformAdministratorAsync(db, hasher, Email, Password, ApiKey);
        await db.SaveChangesAsync();

        Assert.Empty(await db.TenantUsers.ToListAsync());
        Assert.Equal(otherTenant, (await db.TenantApiKeys.SingleAsync()).TenantId);
    }

    private static FlowOSDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FlowOSDbContext(options);
    }
}
