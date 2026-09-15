using FlowOS.Domain.Entities;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Services;

public sealed record WorkflowContextCompilationPackage(
    WorkflowDefinition WorkflowDefinition,
    StateMachineDefinition StateMachineDefinition,
    IReadOnlyList<EventDefinition> EventDefinitions,
    string ContentHash);
