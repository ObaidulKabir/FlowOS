using FlowOS.Domain.Entities;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Services;

/// <summary>
/// The immutable Work and Law artifacts compiled from one ordinary WorkflowClass version.
/// </summary>
public sealed record WorkflowClassCompilationPackage(
    WorkflowDefinition WorkflowDefinition,
    StateMachineDefinition StateMachineDefinition);
