using System;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Services;

/// <summary>
/// Compiles a published WorkflowClass blueprint into a runtime WorkflowDefinition.
/// </summary>
public static class WorkflowClassCompiler
{
    public static WorkflowDefinition MapToRuntimeDefinition(WorkflowClass wc)
    {
        var version = WorkflowVersion.Parse(wc.Version);

        var def = new WorkflowDefinition(
            wc.TenantId,
            wc.Name,
            version.RuntimeVersion,
            wc.Definition.Workflow.StartStepId
        );

        foreach (var stepBp in wc.Definition.Workflow.Steps)
        {
            if (!Enum.TryParse<WorkflowStepType>(stepBp.StepType, true, out var stepType))
            {
                if (stepBp.StepType.Equals("Action", StringComparison.OrdinalIgnoreCase))
                    stepType = WorkflowStepType.Command;
                else
                    throw new InvalidOperationException($"Invalid StepType '{stepBp.StepType}' in step '{stepBp.StepId}'");
            }

            var stepDef = new WorkflowStepDefinition(stepBp.StepId, stepType)
            {
                AllowedRoles = stepBp.RequiredRoles,
                NextSteps = stepBp.NextSteps,
                Conditions = stepBp.Conditions,
                Sla = stepBp.Sla != null ? new StepSlaDefinition(
                    stepBp.Sla.Duration,
                    stepBp.Sla.TimeoutEvent,
                    stepBp.Sla.EscalationStepId,
                    stepBp.Sla.EscalationRole,
                    stepBp.Sla.IsInterrupting) : null
            };
            def.AddStep(stepDef);
        }

        def.Publish();
        return def;
    }
}
