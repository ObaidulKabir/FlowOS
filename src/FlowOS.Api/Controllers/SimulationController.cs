using System.Text.Json;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.DTOs;
using FlowOS.Application.Services;
using FlowOS.Core.Interfaces;
using FlowOS.Core.Security;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/simulations")]
public class SimulationController : ControllerBase
{
    private readonly IWorkflowContextSimulationService _simulationService;
    private readonly AiBusinessContextGenerator _aiGenerator;
    private readonly ICurrentUser _currentUser;

    public SimulationController(
        IWorkflowContextSimulationService simulationService,
        AiBusinessContextGenerator aiGenerator,
        ICurrentUser currentUser) =>
        (_simulationService, _aiGenerator, _currentUser) = (simulationService, aiGenerator, currentUser);

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
        var result = await _simulationService.SimulateAsync(
            effectiveTenant,
            new WorkflowContextSimulationRequest(InitialPayload: request.InitialPayload),
            CancellationToken.None);
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

        var result = await _simulationService.SimulateAsync(
            effectiveTenant,
            new WorkflowContextSimulationRequest(InitialPayload: simRequest.InitialPayload),
            CancellationToken.None);
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
