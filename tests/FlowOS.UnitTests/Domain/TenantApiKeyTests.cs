using System;
using System.Collections.Generic;
using FlowOS.Domain.Entities;
using Xunit;

namespace FlowOS.UnitTests.Domain;

public class TenantApiKeyTests
{
    [Fact]
    public void Constructor_WithApplicationDetails_SetsDefaultsAndAttributes()
    {
        var tenantId = Guid.NewGuid();
        var rawKey = TenantApiKey.GenerateRawKey();

        var key = new TenantApiKey(
            tenantId, 
            "ERP Key", 
            rawKey, 
            applicationName: "SAP Connector", 
            environment: "Staging", 
            scopes: new[] { "workflow:start", "event:publish" },
            expiresAt: DateTime.UtcNow.AddDays(30)
        );

        Assert.Equal(tenantId, key.TenantId);
        Assert.Equal("ERP Key", key.Name);
        Assert.Equal("SAP Connector", key.ApplicationName);
        Assert.Equal("Staging", key.Environment);
        Assert.Equal(2, key.Scopes.Count);
        Assert.Contains("workflow:start", key.Scopes);
        Assert.Contains("event:publish", key.Scopes);
        Assert.False(key.IsExpired);
        Assert.False(key.IsRevoked);
        Assert.True(key.IsActive);
        Assert.NotNull(key.MaskedKey);
        Assert.StartsWith("flw_live_", key.KeyPrefix);
    }

    [Fact]
    public void HasScope_WithWildcard_MatchesEverything()
    {
        var key = new TenantApiKey(Guid.NewGuid(), "Admin Key", TenantApiKey.GenerateRawKey(), scopes: new[] { "*" });

        Assert.True(key.HasScope("workflow:start"));
        Assert.True(key.HasScope("event:publish"));
        Assert.True(key.HasScope("random:custom:scope"));
    }

    [Fact]
    public void HasScope_WithSpecificScope_MatchesExactAndCategoryWildcard()
    {
        var key = new TenantApiKey(
            Guid.NewGuid(), 
            "Workflow Bot", 
            TenantApiKey.GenerateRawKey(), 
            scopes: new[] { "workflow:*", "event:publish" }
        );

        Assert.True(key.HasScope("workflow:start"));
        Assert.True(key.HasScope("workflow:read"));
        Assert.True(key.HasScope("event:publish"));
        Assert.False(key.HasScope("event:delete"));
        Assert.False(key.HasScope("admin:manage"));
    }

    [Fact]
    public void HasScope_WhenMissingScope_ReturnsFalse()
    {
        var key = new TenantApiKey(
            Guid.NewGuid(), 
            "Read Only Key", 
            TenantApiKey.GenerateRawKey(), 
            scopes: new[] { "workflow:read" }
        );

        Assert.True(key.HasScope("workflow:read"));
        Assert.False(key.HasScope("workflow:start"));
        Assert.False(key.HasScope("event:publish"));
    }

    [Fact]
    public void Expiration_And_Revocation_Lifecycle()
    {
        var expiredKey = new TenantApiKey(
            Guid.NewGuid(), 
            "Old Key", 
            TenantApiKey.GenerateRawKey(), 
            expiresAt: DateTime.UtcNow.AddMinutes(-5)
        );

        Assert.True(expiredKey.IsExpired);
        Assert.False(expiredKey.IsActive);

        var activeKey = new TenantApiKey(
            Guid.NewGuid(), 
            "Active Key", 
            TenantApiKey.GenerateRawKey(), 
            expiresAt: DateTime.UtcNow.AddDays(5)
        );

        Assert.False(activeKey.IsExpired);
        Assert.True(activeKey.IsActive);

        activeKey.Revoke();
        Assert.True(activeKey.IsRevoked);
        Assert.False(activeKey.IsActive);
    }
}
