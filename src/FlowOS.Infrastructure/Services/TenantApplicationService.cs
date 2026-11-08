using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public sealed class TenantApplicationService : ITenantApplicationService
{
    private readonly FlowOSDbContext _db;
    private readonly TimeProvider _time;

    public TenantApplicationService(FlowOSDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<TenantApplicationDto>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        var applications = await _db.TenantApplications
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);
        var counts = await ActiveKeyCountsAsync(tenantId, ct);
        return applications.Select(a => ToDto(a, counts)).ToList();
    }

    public async Task<TenantApplicationDto?> GetAsync(Guid tenantId, Guid applicationId, CancellationToken ct = default)
    {
        var application = await _db.TenantApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == applicationId, ct);
        if (application == null)
            return null;
        return ToDto(application, await ActiveKeyCountsAsync(tenantId, ct));
    }

    public async Task<TenantApplicationDto> CreateAsync(Guid tenantId, string name, string? description, CancellationToken ct = default)
    {
        var application = Build(() => new TenantApplication(tenantId, name, description, Now()));
        await EnsureNameFreeAsync(tenantId, application.Name, exceptId: null, ct);

        _db.TenantApplications.Add(application);
        await _db.SaveChangesAsync(ct);
        return ToDto(application, new Dictionary<Guid, int>());
    }

    public async Task<TenantApplicationDto> UpdateAsync(Guid tenantId, Guid applicationId, string name, string? description, CancellationToken ct = default)
    {
        var application = await FindAsync(tenantId, applicationId, ct);
        Build(() => { application.Update(name, description, Now()); return application; });
        await EnsureNameFreeAsync(tenantId, application.Name, exceptId: applicationId, ct);

        var keys = await _db.TenantApiKeys
            .Where(k => k.TenantId == tenantId && k.ApplicationId == applicationId)
            .ToListAsync(ct);
        foreach (var key in keys)
            key.RelabelApplication(application.Name);

        await _db.SaveChangesAsync(ct);
        return ToDto(application, await ActiveKeyCountsAsync(tenantId, ct));
    }

    public async Task DeleteAsync(Guid tenantId, Guid applicationId, CancellationToken ct = default)
    {
        var application = await FindAsync(tenantId, applicationId, ct);
        var now = Now();
        var keys = await _db.TenantApiKeys
            .Where(k => k.TenantId == tenantId && k.ApplicationId == applicationId)
            .ToListAsync(ct);

        var active = keys.Count(k => !k.IsRevoked && (k.ExpiresAt == null || k.ExpiresAt > now));
        if (active > 0)
        {
            throw new TenantApplicationException(
                TenantApplicationErrorCodes.InUse,
                $"Application '{application.Name}' still has {active} active API key(s). Revoke them first.");
        }

        foreach (var key in keys)
            key.DetachFromApplication();

        _db.TenantApplications.Remove(application);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<TenantApplication> FindAsync(Guid tenantId, Guid applicationId, CancellationToken ct)
    {
        return await _db.TenantApplications
                   .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == applicationId, ct)
               ?? throw new TenantApplicationException(
                   TenantApplicationErrorCodes.NotFound,
                   "Application not found.");
    }

    private async Task EnsureNameFreeAsync(Guid tenantId, string name, Guid? exceptId, CancellationToken ct)
    {
        var lowered = name.ToLower();
        var taken = await _db.TenantApplications.AnyAsync(
            a => a.TenantId == tenantId && a.Id != exceptId && a.Name.ToLower() == lowered,
            ct);
        if (taken)
        {
            throw new TenantApplicationException(
                TenantApplicationErrorCodes.NameTaken,
                $"An application named '{name}' already exists.");
        }
    }

    private async Task<Dictionary<Guid, int>> ActiveKeyCountsAsync(Guid tenantId, CancellationToken ct)
    {
        var now = Now();
        return await _db.TenantApiKeys
            .AsNoTracking()
            .Where(k => k.TenantId == tenantId &&
                        k.ApplicationId != null &&
                        !k.IsRevoked &&
                        (k.ExpiresAt == null || k.ExpiresAt > now))
            .GroupBy(k => k.ApplicationId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    private static TenantApplication Build(Func<TenantApplication> action)
    {
        try
        {
            return action();
        }
        catch (ArgumentException ex)
        {
            throw new TenantApplicationException(TenantApplicationErrorCodes.Invalid, ex.Message);
        }
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private static TenantApplicationDto ToDto(TenantApplication a, IReadOnlyDictionary<Guid, int> counts) =>
        new(a.Id, a.TenantId, a.Name, a.Description, a.CreatedAtUtc, a.UpdatedAtUtc,
            counts.TryGetValue(a.Id, out var count) ? count : 0);
}
