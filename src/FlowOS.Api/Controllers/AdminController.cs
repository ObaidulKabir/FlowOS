using System;
using System.IO;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Queries.Admin;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/admin")]
// [Authorize(Roles = "Admin")] // TODO: Add real auth
public class AdminController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly IServiceProvider _serviceProvider;
    private readonly FlowOSDbContext _context;

    public AdminController(IMediator mediator, ICurrentUser currentUser, IServiceProvider serviceProvider, FlowOSDbContext context)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _serviceProvider = serviceProvider;
        _context = context;
    }

    [HttpPost("config/publish")]
    public async Task<IActionResult> PublishConfig()
    {
        // This endpoint manually triggers config loading
        // Suitable for Prod deployment pipelines
        
        try 
        {
             // Locate config folder relative to execution
             // In Prod, this might be a mounted volume or specific path
             var configRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "flowos-config"); 
             // Adjust path logic as needed for environment
             if (!Directory.Exists(configRoot))
             {
                  // Try standard dev path
                  configRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "flowos-config");
             }
             
             if (!Directory.Exists(configRoot))
             {
                 return NotFound("Configuration directory not found.");
             }

             var logger = _serviceProvider.GetRequiredService<ILogger<ConfigurationLoader>>();
             var loader = new ConfigurationLoader(_context, logger, configRoot);
             
             await loader.LoadAllAsync(_currentUser.TenantId);
             
             return Ok("Configuration published successfully.");
        }
        catch (Exception ex)
        {
            return BadRequest($"Configuration publish failed: {ex.Message}");
        }
    }

    [HttpGet("workflows")]
    public async Task<IActionResult> GetWorkflows()
    {
        var tenantId = _currentUser.TenantId;
        var query = new GetAdminWorkflowsQuery { TenantId = tenantId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("workflows/{id}")]
    public async Task<IActionResult> GetWorkflowDetail(Guid id)
    {
        var tenantId = _currentUser.TenantId;
        var query = new GetAdminWorkflowDetailQuery(id, tenantId);
        var result = await _mediator.Send(query);
        
        if (result == null) return NotFound();
        
        return Ok(result);
    }

    [HttpGet("state-machines")]
    public async Task<IActionResult> GetAllStateMachines()
    {
        var query = new GetAllAdminStateMachinesQuery();
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("state-machines/{entityType}")]
    public async Task<IActionResult> GetStateMachine(string entityType)
    {
        var query = new GetAdminStateMachineQuery(entityType);
        var result = await _mediator.Send(query);
        
        if (result == null) return NotFound();
        
        return Ok(result);
    }

    [HttpGet("policies")]
    public async Task<IActionResult> GetPolicies()
    {
        var tenantId = _currentUser.TenantId;
        var query = new GetAdminPoliciesQuery { TenantId = tenantId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetEvents()
    {
        var tenantId = _currentUser.TenantId;
        var query = new GetAdminEventsQuery { TenantId = tenantId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }
}
