using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantsController : ControllerBase
{
    private readonly FlowOSDbContext _context;

    public TenantsController(FlowOSDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> ListTenants()
    {
        var tenants = await _context.Tenants
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var tenantIds = tenants.Select(t => t.TenantId).ToList();

        var keys = await _context.TenantApiKeys
            .AsNoTracking()
            .Where(k => tenantIds.Contains(k.TenantId) && !k.IsRevoked)
            .ToListAsync();

        var keyLookup = keys.ToLookup(k => k.TenantId);

        var result = tenants.Select(t => new TenantDto
        {
            TenantId = t.TenantId,
            Name = t.Name,
            Status = t.Status.ToString(),
            CreatedAt = t.CreatedAt,
            KeyCount = keyLookup[t.TenantId].Count(),
            Keys = keyLookup[t.TenantId].Select(k => new TenantApiKeyDto
            {
                Id = k.Id,
                Name = k.Name,
                ApplicationName = k.ApplicationName,
                Environment = k.Environment,
                Scopes = k.Scopes,
                MaskedKey = k.MaskedKey,
                KeyPrefix = k.KeyPrefix,
                CreatedAt = k.CreatedAt,
                ExpiresAt = k.ExpiresAt,
                LastUsedAt = k.LastUsedAt,
                IsRevoked = k.IsRevoked,
                IsExpired = k.IsExpired,
                IsActive = k.IsActive
            }).ToList()
        });

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> RegisterTenant([FromBody] RegisterTenantRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Tenant name is required.");

        var existing = await _context.Tenants
            .AnyAsync(t => t.Name.ToLower() == request.Name.Trim().ToLower());
        if (existing)
            return Conflict($"A tenant with name '{request.Name}' already exists.");

        var tenant = new Tenant(request.Name.Trim());
        _context.Tenants.Add(tenant);

        var rawKey = TenantApiKey.GenerateRawKey();
        var keyName = string.IsNullOrWhiteSpace(request.KeyName) ? "Default API Key" : request.KeyName.Trim();
        var appName = string.IsNullOrWhiteSpace(request.ApplicationName) ? "Default Application" : request.ApplicationName.Trim();
        var env = string.IsNullOrWhiteSpace(request.Environment) ? "Production" : request.Environment.Trim();
        var scopes = request.Scopes != null && request.Scopes.Any() ? request.Scopes : new List<string> { "*" };
        DateTime? expiresAt = request.ExpiresInDays.HasValue && request.ExpiresInDays.Value > 0
            ? DateTime.UtcNow.AddDays(request.ExpiresInDays.Value)
            : null;

        var apiKey = new TenantApiKey(tenant.TenantId, keyName, rawKey, appName, env, scopes, expiresAt);
        _context.TenantApiKeys.Add(apiKey);

        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTenantById), new { id = tenant.TenantId }, new RegisterTenantResponse
        {
            Tenant = new TenantDto
            {
                TenantId = tenant.TenantId,
                Name = tenant.Name,
                Status = tenant.Status.ToString(),
                CreatedAt = tenant.CreatedAt,
                KeyCount = 1,
                Keys = new List<TenantApiKeyDto>
                {
                    new()
                    {
                        Id = apiKey.Id,
                        Name = apiKey.Name,
                        ApplicationName = apiKey.ApplicationName,
                        Environment = apiKey.Environment,
                        Scopes = apiKey.Scopes,
                        MaskedKey = apiKey.MaskedKey,
                        KeyPrefix = apiKey.KeyPrefix,
                        CreatedAt = apiKey.CreatedAt,
                        ExpiresAt = apiKey.ExpiresAt,
                        LastUsedAt = null,
                        IsRevoked = false,
                        IsExpired = false,
                        IsActive = true
                    }
                }
            },
            ApiKey = rawKey
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTenantById(Guid id)
    {
        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return NotFound("Tenant not found.");

        var keys = await _context.TenantApiKeys
            .Where(k => k.TenantId == id && !k.IsRevoked)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new TenantApiKeyDto
            {
                Id = k.Id,
                Name = k.Name,
                ApplicationName = k.ApplicationName,
                Environment = k.Environment,
                Scopes = k.Scopes,
                MaskedKey = k.MaskedKey,
                KeyPrefix = k.KeyPrefix,
                CreatedAt = k.CreatedAt,
                ExpiresAt = k.ExpiresAt,
                LastUsedAt = k.LastUsedAt,
                IsRevoked = k.IsRevoked,
                IsExpired = k.IsExpired,
                IsActive = k.IsActive
            })
            .ToListAsync();

        return Ok(new TenantDto
        {
            TenantId = tenant.TenantId,
            Name = tenant.Name,
            Status = tenant.Status.ToString(),
            CreatedAt = tenant.CreatedAt,
            KeyCount = keys.Count,
            Keys = keys
        });
    }

    [HttpGet("{id}/keys")]
    public async Task<IActionResult> ListKeys(Guid id)
    {
        var tenantExists = await _context.Tenants.AnyAsync(t => t.TenantId == id);
        if (!tenantExists) return NotFound("Tenant not found.");

        var keys = await _context.TenantApiKeys
            .Where(k => k.TenantId == id)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new TenantApiKeyDto
            {
                Id = k.Id,
                Name = k.Name,
                ApplicationName = k.ApplicationName,
                Environment = k.Environment,
                Scopes = k.Scopes,
                MaskedKey = k.MaskedKey,
                KeyPrefix = k.KeyPrefix,
                CreatedAt = k.CreatedAt,
                ExpiresAt = k.ExpiresAt,
                LastUsedAt = k.LastUsedAt,
                IsRevoked = k.IsRevoked,
                IsExpired = k.IsExpired,
                IsActive = k.IsActive
            })
            .ToListAsync();

        return Ok(keys);
    }

    [HttpPost("{id}/keys")]
    public async Task<IActionResult> GenerateKey(Guid id, [FromBody] GenerateKeyRequest request)
    {
        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return NotFound("Tenant not found.");

        var rawKey = TenantApiKey.GenerateRawKey();
        var keyName = string.IsNullOrWhiteSpace(request.Name) ? "API Key" : request.Name.Trim();
        var appName = string.IsNullOrWhiteSpace(request.ApplicationName) ? "Default Application" : request.ApplicationName.Trim();
        var env = string.IsNullOrWhiteSpace(request.Environment) ? "Production" : request.Environment.Trim();
        var scopes = request.Scopes != null && request.Scopes.Any() ? request.Scopes : new List<string> { "*" };
        DateTime? expiresAt = request.ExpiresInDays.HasValue && request.ExpiresInDays.Value > 0
            ? DateTime.UtcNow.AddDays(request.ExpiresInDays.Value)
            : null;

        var apiKey = new TenantApiKey(id, keyName, rawKey, appName, env, scopes, expiresAt);
        _context.TenantApiKeys.Add(apiKey);

        await _context.SaveChangesAsync();

        return Ok(new CreateKeyResponse
        {
            Id = apiKey.Id,
            TenantId = id,
            Name = apiKey.Name,
            ApplicationName = apiKey.ApplicationName,
            Environment = apiKey.Environment,
            Scopes = apiKey.Scopes,
            ApiKey = rawKey,
            MaskedKey = apiKey.MaskedKey,
            CreatedAt = apiKey.CreatedAt,
            ExpiresAt = apiKey.ExpiresAt
        });
    }

    [HttpDelete("{id}/keys/{keyId}")]
    public async Task<IActionResult> RevokeKey(Guid id, Guid keyId)
    {
        var key = await _context.TenantApiKeys.FirstOrDefaultAsync(k => k.TenantId == id && k.Id == keyId);
        if (key == null) return NotFound("API Key not found.");

        key.Revoke();
        await _context.SaveChangesAsync();

        return NoContent();
    }
}

public class RegisterTenantRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public string? KeyName { get; set; }
    public string? ApplicationName { get; set; }
    public string? Environment { get; set; }
    public List<string>? Scopes { get; set; }
    public int? ExpiresInDays { get; set; }
}

public class GenerateKeyRequest
{
    [Required]
    public string Name { get; set; } = "API Key";
    public string? ApplicationName { get; set; }
    public string? Environment { get; set; }
    public List<string>? Scopes { get; set; }
    public int? ExpiresInDays { get; set; }
}

public class TenantDto
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int KeyCount { get; set; }
    public List<TenantApiKeyDto> Keys { get; set; } = new();
}

public class TenantApiKeyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    public string MaskedKey { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsRevoked { get; set; }
    public bool IsExpired { get; set; }
    public bool IsActive { get; set; }
}

public class RegisterTenantResponse
{
    public TenantDto Tenant { get; set; } = new();
    public string ApiKey { get; set; } = string.Empty;
}

public class CreateKeyResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    public string ApiKey { get; set; } = string.Empty;
    public string MaskedKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
