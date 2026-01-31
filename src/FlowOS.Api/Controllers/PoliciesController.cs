using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using FlowOS.Core.Interfaces; // Changed namespace
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/policies")]
public class PoliciesController : ControllerBase
{
    private readonly FlowOSDbContext _context;
    private readonly ICurrentUser _currentUser;

    public PoliciesController(FlowOSDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> CreatePolicy([FromBody] CreatePolicyRequest request)
    {
        var tenantId = _currentUser.TenantId;

        if (await _context.Policies.AnyAsync(p => p.TenantId == tenantId && p.Name == request.Name))
        {
            return Conflict($"Policy '{request.Name}' already exists.");
        }

        var policy = new Policy(tenantId, request.Name, request.ConditionJson);
        
        _context.Policies.Add(policy);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPolicy), new { id = policy.Id }, new { policy.Id });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPolicy(Guid id)
    {
        var policy = await _context.Policies
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _currentUser.TenantId);

        if (policy == null) return NotFound();

        return Ok(policy);
    }
}

public class CreatePolicyRequest
{
    public string Name { get; set; } = string.Empty;
    public string ConditionJson { get; set; } = "{}";
}
