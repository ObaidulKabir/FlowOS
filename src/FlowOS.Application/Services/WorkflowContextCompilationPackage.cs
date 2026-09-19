using FlowOS.Domain.Entities;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Services;

/// <summary>
/// The compiled runtime artifacts for one context-binding revision. Business-context role
/// declarations ride along on <see cref="WorkflowDefinition.BusinessRoles"/> — they are compiled
/// metadata, not a database write, so there is nothing further to report here about them.
/// </summary>
public sealed record WorkflowContextCompilationPackage(
    WorkflowDefinition WorkflowDefinition,
    StateMachineDefinition StateMachineDefinition,
    IReadOnlyList<EventDefinition> EventDefinitions,
    string ContentHash);
