using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Billing;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Interfaces;
using FlowOS.Core.Security;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.Infrastructure.Services.Security;
using FlowOS.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly FlowOSDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly TenantSecurityProvisioningService _securityProvisioning;
    private readonly ITenantBackupService _backups;
    private readonly ITenantSecretProtector _secretProtector;
    private readonly ITenantPlanActivationService _planActivation;

    public TenantsController(
        FlowOSDbContext context,
        ICurrentUser currentUser,
        TenantSecurityProvisioningService securityProvisioning,
        ITenantBackupService backups,
        ITenantSecretProtector secretProtector,
        ITenantPlanActivationService planActivation)
    {
        _context = context;
        _currentUser = currentUser;
        _securityProvisioning = securityProvisioning;
        _backups = backups;
        _secretProtector = secretProtector;
        _planActivation = planActivation;
    }

    [HttpGet]
    public async Task<IActionResult> ListTenants([FromQuery] string? externalRef = null)
    {
        if (!IsPlatformAdmin())
            return Forbid();

        var query = _context.Tenants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(externalRef))
        {
            var reference = externalRef.Trim();
            query = query.Where(t => t.ExternalTenantRef == reference); // CR-14
        }

        var tenants = await query
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
            ExternalTenantRef = t.ExternalTenantRef,
            Status = t.Status.ToString(),
            Plan = t.Plan.ToString(),
            BillingStatus = t.BillingStatus.ToString(),
            CanRunRuntime = t.CanRunRuntime,
            CreatedAt = t.CreatedAt,
            PlanRequest = t.GetPlanRequest(),
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
        if (!IsPlatformAdmin())
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Tenant name is required.");

        // CR-14: provisioning by the caller's reference is idempotent: a retry finds the tenant it already made.
        var externalRef = string.IsNullOrWhiteSpace(request.ExternalTenantRef) ? null : request.ExternalTenantRef.Trim();
        if (externalRef?.Length > Tenant.ExternalTenantRefMaxLength)
            return BadRequest($"externalTenantRef must be at most {Tenant.ExternalTenantRefMaxLength} characters.");
        if (externalRef != null)
        {
            var mapped = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.ExternalTenantRef == externalRef);
            if (mapped != null)
            {
                return Conflict(new
                {
                    code = "TENANT-EXISTS",
                    tenantId = mapped.TenantId,
                    externalTenantRef = externalRef,
                    message = "A tenant with this externalTenantRef already exists. Generate a key for it with POST /api/tenants/{id}/keys if you need one."
                });
            }
        }

        var existing = await _context.Tenants
            .AnyAsync(t => t.Name.ToLower() == request.Name.Trim().ToLower());
        if (existing)
            return Conflict($"A tenant with name '{request.Name}' already exists.");

        var tenant = new Tenant(request.Name.Trim());
        tenant.SetExternalTenantRef(externalRef);
        tenant.ProtectWebhookSigningSecret(_secretProtector);
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
        await _securityProvisioning.EnsureTenantAsync(tenant.TenantId);

        return CreatedAtAction(nameof(GetTenantById), new { id = tenant.TenantId }, new RegisterTenantResponse
        {
            Tenant = new TenantDto
            {
                TenantId = tenant.TenantId,
                Name = tenant.Name,
                ExternalTenantRef = tenant.ExternalTenantRef,
                Status = tenant.Status.ToString(),
                Plan = tenant.Plan.ToString(),
                BillingStatus = tenant.BillingStatus.ToString(),
                CanRunRuntime = tenant.CanRunRuntime,
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
        if (!CanAccessTenant(id))
            return Forbid();

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
            ExternalTenantRef = tenant.ExternalTenantRef,
            Status = tenant.Status.ToString(),
            Plan = tenant.Plan.ToString(),
            BillingStatus = tenant.BillingStatus.ToString(),
            CanRunRuntime = tenant.CanRunRuntime,
            CreatedAt = tenant.CreatedAt,
            PlanRequest = tenant.GetPlanRequest(),
            KeyCount = keys.Count,
            Keys = keys
        });
    }

    public sealed record SetExternalTenantRefRequest(string? ExternalTenantRef);

    /// <summary>CR-14: set or clear (null) the calling system's reference for an existing tenant.</summary>
    [HttpPut("{id}/external-ref")]
    public async Task<IActionResult> SetExternalTenantRef(Guid id, [FromBody] SetExternalTenantRefRequest request)
    {
        if (!IsPlatformAdmin())
            return Forbid();

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantId == id);
        if (tenant == null)
            return NotFound();

        var reference = string.IsNullOrWhiteSpace(request.ExternalTenantRef) ? null : request.ExternalTenantRef.Trim();
        if (reference != null &&
            await _context.Tenants.AnyAsync(t => t.ExternalTenantRef == reference && t.TenantId != id))
        {
            return Conflict(new { code = "TENANT-EXISTS", message = "Another tenant already uses this externalTenantRef." });
        }

        try
        {
            tenant.SetExternalTenantRef(reference);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        await _context.SaveChangesAsync();
        return Ok(new { tenantId = id, externalTenantRef = tenant.ExternalTenantRef });
    }

    [HttpGet("{id}/keys")]
    public async Task<IActionResult> ListKeys(Guid id)
    {
        if (!CanAccessTenant(id))
            return Forbid();

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
        if (!CanAccessTenant(id))
            return Forbid();

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
        if (!CanAccessTenant(id))
            return Forbid();

        var key = await _context.TenantApiKeys.FirstOrDefaultAsync(k => k.TenantId == id && k.Id == keyId);
        if (key == null) return NotFound("API Key not found.");

        key.Revoke();
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id:guid}/plan-request")]
    public async Task<IActionResult> SubmitPlanRequest(Guid id, [FromBody] SubmitPlanRequest request)
    {
        if (!CanAccessTenant(id))
            return Forbid();

        var result = await _planActivation.SubmitAsync(
            id,
            new PlanActivationInput(request?.Plan, request?.PaymentReference, request?.BillingContactEmail, request?.Notes),
            idempotent: false);

        return result.Outcome switch
        {
            PlanActivationOutcome.TenantNotFound => NotFound("Tenant not found."),
            PlanActivationOutcome.InvalidPlan => BadRequest("Plan must be Starter, Builder, Team, Growth, Scale, or Enterprise."),
            _ => Ok(result.PlanRequest)
        };
    }

    [HttpGet("{id:guid}/plan-request")]
    public async Task<IActionResult> GetPlanRequest(Guid id)
    {
        if (!CanAccessTenant(id))
            return Forbid();

        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return NotFound("Tenant not found.");

        var req = tenant.GetPlanRequest();
        if (req == null) return NotFound("No plan request found for this tenant.");

        return Ok(req);
    }

    [HttpGet("{id}/backup")]
    public async Task<IActionResult> DownloadBackup(Guid id, CancellationToken cancellationToken)
    {
        if (!CanAccessTenant(id))
            return Forbid();

        var denied = RequireBackupConfirmation(TenantBackupConfirmation.Download);
        if (denied != null)
            return denied;

        try
        {
            // CR-15: encrypt with the caller's passphrase. Outside Development a passphrase is required.
            var passphrase = Request.Headers[TenantBackupEncryption.PassphraseHeader].ToString();
            if (string.IsNullOrEmpty(passphrase) && BackupEncryptionRequired())
            {
                return BadRequest(new
                {
                    code = "BACKUP-PASSPHRASE-REQUIRED",
                    message = $"Backups are encrypted on this site. Send a passphrase of at least {TenantBackupEncryption.MinimumPassphraseLength} characters in the {TenantBackupEncryption.PassphraseHeader} header."
                });
            }

            var json = await _backups.ExportJsonAsync(id, cancellationToken);
            var encrypted = !string.IsNullOrEmpty(passphrase);
            if (encrypted)
                json = TenantBackupEncryption.Encrypt(json, passphrase);
            var fileName = $"flowos-tenant-backup-{id:N}-{DateTime.UtcNow:yyyyMMddHHmmss}{(encrypted ? ".encrypted" : string.Empty)}.json";
            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", fileName);
        }
        catch (TenantBackupException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("{id}/backup/restore")]
    public async Task<IActionResult> RestoreBackup(Guid id, CancellationToken cancellationToken)
    {
        if (!CanAccessTenant(id))
            return Forbid();

        var denied = RequireBackupConfirmation(TenantBackupConfirmation.Restore);
        if (denied != null)
            return denied;

        string json;
        using (var reader = new System.IO.StreamReader(Request.Body))
            json = await reader.ReadToEndAsync(cancellationToken);

        try
        {
            if (TenantBackupEncryption.IsEncrypted(json))
                json = TenantBackupEncryption.Decrypt(json, Request.Headers[TenantBackupEncryption.PassphraseHeader].ToString());

            var result = await _backups.RestoreJsonAsync(id, json, cancellationToken);
            return Ok(result);
        }
        catch (TenantBackupException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
    }

    /// <summary>CR-15: FlowOS:Backup:RequireEncryption; defaults to true outside Development.</summary>
    private bool BackupEncryptionRequired()
    {
        var configured = HttpContext.RequestServices.GetService<IConfiguration>()?["FlowOS:Backup:RequireEncryption"];
        if (bool.TryParse(configured, out var required)) return required;
        return !HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
    }

    private IActionResult? RequireBackupConfirmation(string expected)
    {
        if (TenantBackupConfirmation.IsConfirmed(Request.Headers[TenantBackupConfirmation.HeaderName], expected))
            return null;

        return BadRequest(new
        {
            code = "BACKUP-CONFIRMATION",
            message = $"Backup {expected} requires header {TenantBackupConfirmation.HeaderName}: {expected}. Acknowledge the confidential file in the dashboard before downloading or restoring it."
        });
    }

    private bool CanAccessTenant(Guid tenantId)
        => tenantId != Guid.Empty &&
           (_currentUser.TenantId == tenantId || IsPlatformAdmin());

    private bool IsPlatformAdmin()
        => TenantIdentityRules.IsPlatformAdministrator(_currentUser.TenantId, _currentUser.Roles);
}

public class RegisterTenantRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;
    /// <summary>CR-14: the calling system's reference for this tenant; makes registration idempotent.</summary>
    public string? ExternalTenantRef { get; set; }
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
    public string? ExternalTenantRef { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string BillingStatus { get; set; } = string.Empty;
    public bool CanRunRuntime { get; set; }
    public DateTime CreatedAt { get; set; }
    public PlanRequestDto? PlanRequest { get; set; }
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
