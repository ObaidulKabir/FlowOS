using System;
using FlowOS.Core.Security;
using Xunit;

namespace FlowOS.UnitTests.Security;

public class JwtSecretRulesTests
{
    private const string Strong = "4f9c2e7a1b8d6f3e0a5c9b2d7e4f1a8c6b3d0e9f";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData(null)]
    public void Missing_secret_stops_the_host_outside_Development(string? environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JwtSecretRules.Resolve(null, environment));
        Assert.Contains(JwtSecretRules.EnvironmentVariable, ex.Message);
    }

    [Theory]
    [InlineData("FlowOS_Super_Secret_Key_For_Tenant_Auth_2026_Minimum_32_Chars!")] // was in appsettings.json
    [InlineData(JwtSecretRules.DevelopmentOnlySecret)]
    public void Published_secret_stops_the_host_in_Production(string published)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JwtSecretRules.Resolve(published, "Production"));
        Assert.Contains("published", ex.Message);
    }

    [Fact]
    public void Short_secret_stops_the_host_in_Production()
    {
        Assert.Throws<InvalidOperationException>(() => JwtSecretRules.Resolve("too-short", "Production"));
    }

    [Fact]
    public void Strong_secret_is_used_in_Production()
    {
        Assert.Equal(Strong, JwtSecretRules.Resolve(Strong, "Production"));
    }

    [Fact]
    public void Development_without_a_secret_uses_the_development_only_value()
    {
        Assert.Equal(JwtSecretRules.DevelopmentOnlySecret, JwtSecretRules.Resolve(null, "Development"));
        Assert.Equal("short-dev-secret", JwtSecretRules.Resolve("short-dev-secret", "development"));
    }
}
