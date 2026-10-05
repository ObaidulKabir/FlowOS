using Microsoft.AspNetCore.Mvc;
using FlowOS.Application.DTOs;
using FlowOS.Application.Services;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/simulations")]
public class SimulationController : ControllerBase
{
    private readonly IWorkflowContextSimulationService _simulationService;
    private readonly AiBusinessContextGenerator _aiGenerator;
    private readonly McpTenantResolver _tenantResolver;

    public SimulationController(
        IWorkflowContextSimulationService simulationService,
        AiBusinessContextGenerator aiGenerator,
        McpTenantResolver tenantResolver) =>
        (_simulationService, _aiGenerator, _tenantResolver) = (simulationService, aiGenerator, tenantResolver);

    /// <summary>
    /// Run a simulation given an explicit payload or business‑context.
    /// The caller may provide `InitialPayload` directly, or reference a generated BusinessContext.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<WorkflowContextSimulationResultDto>> Run(
        [FromBody] RunSimulationRequest request,
        [FromQuery] Guid? tenantId)
    {
        var effectiveTenant = ResolveEffectiveTenant(request.TenantId ?? tenantId);
        var result = await _simulationService.SimulateAsync(effectiveTenant, request, CancellationToken.None);
        return Ok(result);
    }

    /// <summary>
    /// Convenience endpoint: generate an AI‑driven BusinessContext and immediately run the simulation.
    /// </summary>
    [HttpPost("run-with-ai")]
    public async Task<ActionResult<WorkflowContextSimulationResultDto>> RunWithAi([
        FromBody] RunSimulationWithAiRequest request,
        [FromQuery] Guid? tenantId)
    {
        var effectiveTenant = ResolveEffectiveTenant(request.TenantId ?? tenantId);
        // 1️⃣ Generate business context via AI
        var generated = await _aiGenerator.GenerateAsync(
            effectiveTenant,
            request.BusinessCaseDescription,
            request.DesiredContextName);

        // 2️⃣ Build a RunSimulationRequest using the generated payload
        var simRequest = new RunSimulationRequest
        {
            TenantId = effectiveTenant,
            WorkflowClassId = request.WorkflowClassId,
            InitialPayload = JsonSerializer.Deserialize<Dictionary<string, object>>(generated.ExamplePayloadJson) ?? new Dictionary<string, object>(),
            // Copy optional fields if needed (roles, events, etc.) – keep defaults for now
        };

        var result = await _simulationService.SimulateAsync(effectiveTenant, simRequest, CancellationToken.None);
        return Ok(result);
    }

    private Guid ResolveEffectiveTenant(Guid? target)
    {
        if (User.IsPlatformAdmin())
        {
            return target ?? _tenantResolver.ResolveFromHeader(HttpContext);
        }
        return _tenantResolver.ResolveFromHeader(HttpContext);
    }
}
