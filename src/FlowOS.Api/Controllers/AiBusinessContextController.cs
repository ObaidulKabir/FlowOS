using FlowOS.Application.DTOs;
using FlowOS.Application.Services;
using FlowOS.Core.Interfaces;
using FlowOS.Core.Security;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/ai/business-context")]
public class AiBusinessContextController : ControllerBase
{
    private readonly AiBusinessContextGenerator _generator;
    private readonly ICurrentUser _currentUser;

    public AiBusinessContextController(AiBusinessContextGenerator generator, ICurrentUser currentUser)
    {
        _generator = generator;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Generates a declarative business‑context (schema + example payload) using an AI agent.
    /// The tenant can optionally specify a TenantId – otherwise the header is used.
    /// </summary>
    [HttpPost("generate")]
    public async Task<ActionResult<GeneratedBusinessContextDto>> Generate(
        [FromBody] GenerateBusinessContextRequest request,
        [FromQuery] Guid? tenantId)
    {
        var effectiveTenant = ResolveEffectiveTenant(request.TenantId ?? tenantId);
        var result = await _generator.GenerateAsync(effectiveTenant, request.BusinessCaseDescription, request.DesiredName);
        return Ok(result);
    }

    private Guid ResolveEffectiveTenant(Guid? target)
    {
        if (!TenantIdentityRules.IsPlatformAdministrator(_currentUser.TenantId, _currentUser.Roles))
            return _currentUser.TenantId;

        if (target.HasValue && target.Value != Guid.Empty)
            return target.Value;

        if (Request.Headers.TryGetValue("x-tenant-id", out var header) &&
            TenantIdentityRules.TryParseTenant(header.ToString(), out var headerTenant) &&
            headerTenant != Guid.Empty)
        {
            return headerTenant;
        }

        return _currentUser.TenantId;
    }
}
