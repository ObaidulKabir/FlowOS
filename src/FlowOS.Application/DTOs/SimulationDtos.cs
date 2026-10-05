using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlowOS.Application.DTOs;

/// <summary>
/// Request model for running a simulation with an explicit payload.
/// </summary>
public class RunSimulationRequest
{
    /// <summary>
    /// Optional tenant identifier – if omitted the request header "x-tenant-id" is used.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Id of the workflow class to simulate.
    /// </summary>
    public Guid WorkflowClassId { get; set; }

    /// <summary>
    /// Initial payload for the simulation (must match the workflow's expected context schema).
    /// </summary>
    public Dictionary<string, object> InitialPayload { get; set; } = new();

    // Additional optional fields (roles, events, etc.) can be added as needed.
}

/// <summary>
/// Request model for generating an AI‑driven business context and immediately running the simulation.
/// </summary>
public class RunSimulationWithAiRequest
{
    public Guid? TenantId { get; set; }
    public Guid WorkflowClassId { get; set; }
    public string BusinessCaseDescription { get; set; } = string.Empty;
    public string DesiredContextName { get; set; } = string.Empty;
}
