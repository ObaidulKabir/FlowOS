using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowOS.Domain.Blueprints;
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
                DecisionProvider = stepBp.DecisionProvider,
                SubWorkflow = stepBp.SubWorkflow == null
                    ? null
                    : new SubWorkflowReferenceDefinition
                    {
                        WorkflowDefinitionId = stepBp.SubWorkflow.WorkflowDefinitionId,
                        WorkflowClassId = stepBp.SubWorkflow.WorkflowClassId,
                        WorkflowName = stepBp.SubWorkflow.WorkflowName,
                        Version = stepBp.SubWorkflow.Version,
                        InputMapping = stepBp.SubWorkflow.InputMapping ?? new Dictionary<string, string>(),
                        OutputMapping = stepBp.SubWorkflow.OutputMapping ?? new Dictionary<string, string>()
                    },
                AllowedRoles = stepBp.RequiredRoles,
                NextSteps = stepBp.NextSteps,
                Conditions = stepBp.Conditions,
                Branches = (stepBp.Branches != null && stepBp.Branches.Any())
                    ? stepBp.Branches
                    : (stepType == WorkflowStepType.Fork ? stepBp.NextSteps.Values.ToList() : new List<string>()),
                JoinPolicy = !string.IsNullOrWhiteSpace(stepBp.JoinPolicy) ? stepBp.JoinPolicy : "WaitAll",
                InboundSteps = stepBp.InboundSteps ?? new List<string>(),
                Sla = stepBp.Sla != null ? new StepSlaDefinition(
                    stepBp.Sla.Duration,
                    stepBp.Sla.TimeoutEvent,
                    stepBp.Sla.EscalationStepId,
                    stepBp.Sla.EscalationRole,
                    stepBp.Sla.IsInterrupting,
                    stepBp.Sla.Reminders?.Select(r => new StepReminderDefinition(r.Duration, r.TriggerEvent)).ToList()) : null,
                OnEntry = stepBp.OnEntry?.Select(a => new StepActionDefinition(a.ActionType)
                {
                    Target = a.Target,
                    Capability = a.Capability,
                    Url = a.Url,
                    Method = a.Method,
                    Template = a.Template,
                    PayloadMapping = a.PayloadMapping,
                    Condition = a.Condition,
                    Headers = a.Headers,
                    SignPayload = a.SignPayload,
                    SecretName = a.SecretName
                }).ToList() ?? new List<StepActionDefinition>(),
                OnExit = stepBp.OnExit?.Select(a => new StepActionDefinition(a.ActionType)
                {
                    Target = a.Target,
                    Capability = a.Capability,
                    Url = a.Url,
                    Method = a.Method,
                    Template = a.Template,
                    PayloadMapping = a.PayloadMapping,
                    Condition = a.Condition,
                    Headers = a.Headers,
                    SignPayload = a.SignPayload,
                    SecretName = a.SecretName
                }).ToList() ?? new List<StepActionDefinition>(),
                OnFailure = stepBp.OnFailure?.Select(a => new StepActionDefinition(a.ActionType)
                {
                    Target = a.Target,
                    Capability = a.Capability,
                    Url = a.Url,
                    Method = a.Method,
                    Template = a.Template,
                    PayloadMapping = a.PayloadMapping,
                    Condition = a.Condition,
                    Headers = a.Headers,
                    SignPayload = a.SignPayload,
                    SecretName = a.SecretName
                }).ToList() ?? new List<StepActionDefinition>()
            };
            def.AddStep(stepDef);
        }

        def.Publish();
        return def;
    }

    public static WorkflowContextCompilationPackage MapToContextRuntimePackage(
        WorkflowClass source,
        WorkflowContextBinding binding,
        WorkflowContextBindingRevision revision)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (binding == null) throw new ArgumentNullException(nameof(binding));
        if (revision == null) throw new ArgumentNullException(nameof(revision));
        if (revision.BindingId != binding.Id)
            throw new InvalidOperationException("Context-binding revision does not belong to the supplied binding.");
        if (revision.SourceWorkflowClassId != source.Id)
            throw new InvalidOperationException("Context-binding revision does not reference the supplied workflow template.");

        var mapping = revision.Definition;
        if (string.IsNullOrWhiteSpace(mapping.EntityType))
            throw new InvalidOperationException("Context binding EntityType is required.");

        var stateMachine = new StateMachineDefinition(
            binding.TenantId,
            mapping.EntityType.Trim(),
            source.Definition.StateMachine.InitialState,
            revision.Revision);

        foreach (var state in source.Definition.StateMachine.States)
        {
            if (!string.Equals(state, stateMachine.InitialState, StringComparison.Ordinal))
            {
                stateMachine.AddState(state);
            }
        }

        foreach (var transition in source.Definition.StateMachine.Transitions)
        {
            var constraints = transition.Constraints != null
                ? new Dictionary<string, string>(transition.Constraints, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(transition.Condition))
            {
                if (TryGetValue(constraints, "Expression", out var existingExpression) &&
                    !string.Equals(existingExpression, transition.Condition, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Transition '{transition.FromState}' -> '{transition.ToState}' declares conflicting Condition and Expression values.");
                }
                constraints["Expression"] = transition.Condition;
            }

            if (TryGetValue(constraints, "Role", out var requiredRole))
            {
                constraints["Role"] = MapValue(requiredRole, mapping.RoleOverrides);
            }

            stateMachine.AddTransition(new StateTransition
            {
                FromState = transition.FromState,
                ToState = transition.ToState,
                EventId = MapValue(transition.EventId, mapping.EventAliases),
                Constraints = constraints
            });
        }
        stateMachine.Publish();

        var workflow = new WorkflowDefinition(
            binding.TenantId,
            binding.Name,
            revision.Revision,
            source.Definition.Workflow.StartStepId);

        foreach (var step in source.Definition.Workflow.Steps)
        {
            if (!Enum.TryParse<WorkflowStepType>(step.StepType, true, out var stepType))
            {
                if (step.StepType.Equals("Action", StringComparison.OrdinalIgnoreCase))
                    stepType = WorkflowStepType.Command;
                else
                    throw new InvalidOperationException($"Invalid StepType '{step.StepType}' in step '{step.StepId}'.");
            }

            var declaredRoles = step.RequiredRoles
                .Concat(step.AllowedRoles ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(x => MapValue(x, mapping.RoleOverrides))
                .ToList();

            var compiledStep = new WorkflowStepDefinition(step.StepId, stepType)
            {
                DecisionProvider = string.IsNullOrWhiteSpace(step.DecisionProvider)
                    ? step.DecisionProvider
                    : MapValue(step.DecisionProvider, mapping.DecisionProviderOverrides),
                SubWorkflow = step.SubWorkflow == null
                    ? null
                    : new SubWorkflowReferenceDefinition
                    {
                        WorkflowDefinitionId = step.SubWorkflow.WorkflowDefinitionId,
                        WorkflowClassId = step.SubWorkflow.WorkflowClassId,
                        WorkflowName = step.SubWorkflow.WorkflowName,
                        Version = step.SubWorkflow.Version,
                        InputMapping = new Dictionary<string, string>(step.SubWorkflow.InputMapping),
                        OutputMapping = new Dictionary<string, string>(step.SubWorkflow.OutputMapping)
                    },
                AllowedRoles = declaredRoles,
                NextSteps = step.NextSteps.ToDictionary(
                    item => MapValue(item.Key, mapping.EventAliases),
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase),
                Conditions = new Dictionary<string, string>(step.Conditions),
                Branches = step.Branches != null && step.Branches.Count > 0
                    ? new List<string>(step.Branches)
                    : stepType == WorkflowStepType.Fork
                        ? step.NextSteps.Values.ToList()
                        : new List<string>(),
                JoinPolicy = string.IsNullOrWhiteSpace(step.JoinPolicy) ? "WaitAll" : step.JoinPolicy,
                InboundSteps = step.InboundSteps != null ? new List<string>(step.InboundSteps) : new List<string>(),
                Sla = step.Sla == null
                    ? null
                    : new StepSlaDefinition(
                        step.Sla.Duration,
                        MapValue(step.Sla.TimeoutEvent, mapping.EventAliases),
                        step.Sla.EscalationStepId,
                        string.IsNullOrWhiteSpace(step.Sla.EscalationRole)
                            ? step.Sla.EscalationRole
                            : MapValue(step.Sla.EscalationRole, mapping.RoleOverrides),
                        step.Sla.IsInterrupting,
                        step.Sla.Reminders?
                            .Select(reminder => new StepReminderDefinition(
                                reminder.Duration,
                                MapValue(reminder.TriggerEvent, mapping.EventAliases)))
                            .ToList()),
                OnEntry = MapActions(step.OnEntry, mapping),
                OnExit = MapActions(step.OnExit, mapping),
                OnFailure = MapActions(step.OnFailure, mapping)
            };

            workflow.AddStep(compiledStep);
        }

        workflow.SetContextLineage(source.Id, revision.Id, stateMachine.Id);
        workflow.Publish();

        var eventDefinitions = source.Definition.Events.Select(eventBlueprint =>
        {
            var contextualEventId = MapValue(eventBlueprint.EventId, mapping.EventAliases);
            var sourceSchema = TryGetValue(
                mapping.EventSourcePayloadSchemas,
                eventBlueprint.EventId,
                out var eventSourceSchema)
                ? eventSourceSchema
                : eventBlueprint.PayloadSchema;

            var eventDefinition = new EventDefinition(
                contextualEventId,
                binding.TenantId,
                eventBlueprint.Name,
                eventBlueprint.Description,
                mapping.EntityType.Trim(),
                eventBlueprint.Category,
                revision.Revision,
                sourceSchema,
                eventBlueprint.IsTerminal);
            eventDefinition.Publish();
            return eventDefinition;
        }).ToList();

        var hashInput = JsonSerializer.Serialize(new
        {
            SourceId = source.Id,
            revision.SourceWorkflowClassVersion,
            SourceDefinition = source.Definition,
            BindingId = binding.Id,
            revision.Revision,
            revision.Definition
        });
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));

        return new WorkflowContextCompilationPackage(workflow, stateMachine, eventDefinitions, contentHash);
    }

    private static List<StepActionDefinition> MapActions(
        IEnumerable<StepActionBlueprint>? actions,
        WorkflowContextBindingDefinition mapping)
    {
        if (actions == null) return new List<StepActionDefinition>();

        return actions.Select(action =>
        {
            var target = action.Target;
            if (!string.IsNullOrWhiteSpace(target))
            {
                if (string.Equals(action.ActionType, "PublishEvent", StringComparison.OrdinalIgnoreCase))
                {
                    target = MapValue(target, mapping.EventAliases);
                }
                else if (string.Equals(action.ActionType, "InvokeCapability", StringComparison.OrdinalIgnoreCase))
                {
                    target = MapCapability(target, mapping);
                }
                else
                {
                    target = MapValue(target, mapping.RoleOverrides);
                }
            }

            return new StepActionDefinition(action.ActionType)
            {
                Target = target,
                Capability = string.IsNullOrWhiteSpace(action.Capability)
                    ? action.Capability
                    : MapCapability(action.Capability, mapping),
                Url = action.Url,
                Method = action.Method,
                Template = action.Template,
                PayloadMapping = action.PayloadMapping == null
                    ? null
                    : new Dictionary<string, string>(action.PayloadMapping),
                Condition = action.Condition,
                Headers = action.Headers == null
                    ? null
                    : new Dictionary<string, string>(action.Headers),
                SignPayload = action.SignPayload,
                SecretName = action.SecretName
            };
        }).ToList();
    }

    private static string MapCapability(string value, WorkflowContextBindingDefinition mapping)
    {
        var explicitMapping = MapValue(value, mapping.CapabilityOverrides);
        if (!string.Equals(explicitMapping, value, StringComparison.OrdinalIgnoreCase))
        {
            return explicitMapping;
        }

        const string eventPrefix = "event.publish.";
        if (value.StartsWith(eventPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var eventId = value[eventPrefix.Length..];
            return eventPrefix + MapValue(eventId, mapping.EventAliases);
        }

        return value;
    }

    private static string MapValue(string value, IReadOnlyDictionary<string, string> mappings)
        => TryGetValue(mappings, value, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
            ? mapped.Trim()
            : value;

    private static bool TryGetValue(
        IReadOnlyDictionary<string, string> values,
        string key,
        out string value)
    {
        foreach (var item in values)
        {
            if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
