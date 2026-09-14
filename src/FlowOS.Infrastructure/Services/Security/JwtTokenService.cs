using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowOS.Security.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.Services.Security;

public class JwtTokenService : IJwtTokenService
{
    private readonly byte[] _keyBytes;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _defaultExpirationHours;
    private readonly ILogger<JwtTokenService> _logger;

    public JwtTokenService(IConfiguration configuration, ILogger<JwtTokenService> logger)
    {
        _logger = logger;
        var secret = configuration["FlowOS:Auth:JwtSecret"] 
                     ?? "FlowOS_Super_Secret_Key_For_Tenant_Auth_2026_Minimum_32_Chars!";
        _issuer = configuration["FlowOS:Auth:JwtIssuer"] ?? "FlowOS";
        _audience = configuration["FlowOS:Auth:JwtAudience"] ?? "FlowOS-Tenants";
        
        if (!int.TryParse(configuration["FlowOS:Auth:TokenExpirationHours"], out _defaultExpirationHours) || _defaultExpirationHours <= 0)
        {
            _defaultExpirationHours = 24;
        }

        _keyBytes = Encoding.UTF8.GetBytes(secret.PadRight(32, '!'));
    }

    public string GenerateToken(Guid userId, string email, string fullName, Guid tenantId, string tenantName, string role, TimeSpan? lifetime = null)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(lifetime ?? TimeSpan.FromHours(_defaultExpirationHours));

        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };

        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(),
            ["email"] = email,
            ["name"] = fullName,
            ["tenant_id"] = tenantId.ToString(),
            ["tenant_name"] = tenantName,
            ["role"] = role,
            ["iss"] = _issuer,
            ["aud"] = _audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expires.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);

        var headerEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var unsignedToken = $"{headerEncoded}.{payloadEncoded}";
        using var hmac = new HMACSHA256(_keyBytes);
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(unsignedToken));
        var signatureEncoded = Base64UrlEncode(signatureBytes);

        return $"{unsignedToken}.{signatureEncoded}";
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        var unsignedToken = $"{parts[0]}.{parts[1]}";
        using var hmac = new HMACSHA256(_keyBytes);
        var computedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(unsignedToken));
        var expectedSignatureEncoded = Base64UrlEncode(computedSignature);

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(parts[2]),
            Encoding.UTF8.GetBytes(expectedSignatureEncoded)))
        {
            _logger.LogWarning("JWT signature validation failed.");
            return null;
        }

        try
        {
            var payloadBytes = Base64UrlDecode(parts[1]);
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("exp", out var expProp))
            {
                var expSeconds = expProp.GetInt64();
                var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (expSeconds < nowSeconds)
                {
                    _logger.LogWarning("JWT token has expired.");
                    return null;
                }
            }

            var claims = new List<Claim>();

            if (root.TryGetProperty("sub", out var subProp))
                claims.Add(new Claim(ClaimTypes.NameIdentifier, subProp.GetString()!));

            if (root.TryGetProperty("email", out var emailProp))
            {
                claims.Add(new Claim(ClaimTypes.Email, emailProp.GetString()!));
                claims.Add(new Claim("email", emailProp.GetString()!));
            }

            if (root.TryGetProperty("name", out var nameProp))
                claims.Add(new Claim(ClaimTypes.Name, nameProp.GetString()!));

            if (root.TryGetProperty("tenant_id", out var tenantProp))
                claims.Add(new Claim("tenant_id", tenantProp.GetString()!));

            if (root.TryGetProperty("tenant_name", out var tenantNameProp))
                claims.Add(new Claim("tenant_name", tenantNameProp.GetString()!));

            if (root.TryGetProperty("role", out var roleProp))
            {
                claims.Add(new Claim(ClaimTypes.Role, roleProp.GetString()!));
                claims.Add(new Claim("role", roleProp.GetString()!));
            }

            var identity = new ClaimsIdentity(claims, "Bearer");
            return new ClaimsPrincipal(identity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse JWT payload.");
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }
}
