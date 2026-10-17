using System.Text;
using System.Text.Json;
using FlowOS.Infrastructure.Services.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowOS.UnitTests.Security;

public class JwtSessionExpiryTests
{
    [Fact]
    public void DashboardSessionToken_ExpiresIn24Hours_AndIsNotAnApplicationKey()
    {
        var service = new JwtTokenService(new ConfigurationBuilder().Build(), NullLogger<JwtTokenService>.Instance);
        var token = service.GenerateToken(
            Guid.NewGuid(),
            "user@example.com",
            "User",
            Guid.NewGuid(),
            "Tenant",
            "User");

        Assert.StartsWith("eyJ", token);
        Assert.DoesNotContain("flw_live_", token);

        var payload = token.Split('.')[1];
        var json = Encoding.UTF8.GetString(PadBase64(payload));
        using var document = JsonDocument.Parse(json);
        var lifetime = document.RootElement.GetProperty("exp").GetInt64()
            - document.RootElement.GetProperty("iat").GetInt64();
        Assert.Equal(24 * 60 * 60, lifetime);
    }

    private static byte[] PadBase64(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }
}
