using Microsoft.AspNetCore.Mvc;
using FlowOS.Application.DTOs;
using FlowOS.Application.Services;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/ai/business-context")]
public class AiBusinessContextController : ControllerBase
{
    private readonly AiBusinessContextGenerator _generator;
    private readonly McpTenantResolver _tenantResolver;

    public AiBusinessContextController(AiBusinessContextGenerator generator, McpTenantResolver tenantResolver) =>
        (_generator, _tenantResolver) = (generator, tenantResolver);

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
        // Re‑use the same helper logic used in other controllers
        if (User.IsPlatformAdmin())
        {
            return target ?? _tenantResolver.ResolveFromHeader(HttpContext);
        }
        return _tenantResolver.ResolveFromHeader(HttpContext);
    }
}
