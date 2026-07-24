using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class PolicyRepository : IPolicyRepository
{
    private readonly FlowOSDbContext _context;

    public PolicyRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<bool> ExistsByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken = default)
        => _context.Policies.AnyAsync(p => p.TenantId == tenantId && p.Name == name, cancellationToken);

    public Task<Policy?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
        => _context.Policies.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);

    public void Add(Policy policy) => _context.Policies.Add(policy);
}
