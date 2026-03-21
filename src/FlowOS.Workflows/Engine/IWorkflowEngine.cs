using FlowOS.Domain.Entities;
using FlowOS.Events.Abstractions;
using FlowOS.Workflows.Domain;

namespace FlowOS.Workflows.Engine
{
    public interface IWorkflowEngine
    {
        WorkflowAdvanceResult Advance(WorkflowInstance instance, WorkflowDefinition definition, IEvent domainEvent, StateMachines.Models.ExecutionContext context, StateMachineDefinition? stateMachineDefinition = null, string? currentEntityState = null);
    }
}