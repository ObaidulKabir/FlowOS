using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
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
                RequiredCapabilities = ResolveStepCapabilities(stepBp, wc.Definition.Events),
                EventRequiredCapabilities = ResolveEventCapabilities(stepBp, wc.Definition.Events),
                NextSteps = stepBp.NextSteps,
                PathLimits = MapPathLimits(stepBp.PathLimits),
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
                OnEntry = MapActions(stepBp.OnEntry),
                OnExit = MapActions(stepBp.OnExit),
                OnFailure = MapActions(stepBp.OnFailure),
                Actor = FlowOS.Domain.Enums.StepActor.Normalize(stepBp.Actor),
                DecisionGuideline = stepBp.DecisionGuideline,
                AutoCommit = MapAutoCommit(stepBp.AutoCommit),
                AgentProvider = string.IsNullOrWhiteSpace(stepBp.AgentProvider) ? null : stepBp.AgentProvider.Trim(),
                AgentPrompt = string.IsNullOrWhiteSpace(stepBp.AgentPrompt) ? null : stepBp.AgentPrompt.Trim(),
                AgentTools = stepBp.AgentTools != null ? new List<string>(stepBp.AgentTools) : new List<string>()
            };
            def.AddStep(stepDef);
        }

        ApplyTemplateBusinessRoles(def, wc.Definition);
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
                        InputMapping = new Dictionary<string, string>(
                            step.SubWorkflow.InputMapping ?? new Dictionary<string, string>()),
                        OutputMapping = new Dictionary<string, string>(
                            step.SubWorkflow.OutputMapping ?? new Dictionary<string, string>())
                    },
                AllowedRoles = declaredRoles,
                RequiredCapabilities = ResolveStepCapabilities(step, source.Definition.Events)
                    .Select(cap => MapCapability(cap, mapping))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                EventRequiredCapabilities = RemapEventCapabilities(
                    ResolveEventCapabilities(step, source.Definition.Events),
                    mapping),
                NextSteps = step.NextSteps.ToDictionary(
                    item => MapValue(item.Key, mapping.EventAliases),
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase),
                PathLimits = MapPathLimits(step.PathLimits, mapping.EventAliases),
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
                OnFailure = MapActions(step.OnFailure, mapping),
                Actor = FlowOS.Domain.Enums.StepActor.Normalize(step.Actor),
                DecisionGuideline = step.DecisionGuideline,
                AutoCommit = MapAutoCommit(step.AutoCommit, mapping.EventAliases),
                AgentProvider = string.IsNullOrWhiteSpace(step.AgentProvider) ? null : step.AgentProvider.Trim(),
                AgentPrompt = string.IsNullOrWhiteSpace(step.AgentPrompt) ? null : step.AgentPrompt.Trim(),
                AgentTools = step.AgentTools != null ? new List<string>(step.AgentTools) : new List<string>()
            };

            workflow.AddStep(compiledStep);
        }

        ApplyTemplateBusinessRoles(workflow, source.Definition, mapping);

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

            var eventName = string.IsNullOrWhiteSpace(eventBlueprint.Name)
                ? contextualEventId
                : eventBlueprint.Name;
            var eventDefinition = new EventDefinition(
                contextualEventId,
                binding.TenantId,
                eventName,
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

    internal static List<string> ResolveStepCapabilities(
        StepBlueprint step,
        IEnumerable<EventBlueprint> events)
    {
        var caps = (step.RequiredCapabilities ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (caps.Count > 0)
            return caps;

        if (!string.Equals(step.StepType, "HumanTask", StringComparison.OrdinalIgnoreCase))
            return caps;

        foreach (var eventId in HumanExitEvents(step))
        {
            caps.AddRange(ResolveEventCapabilitiesForId(eventId, events));
        }

        return caps.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static Dictionary<string, List<string>> ResolveEventCapabilities(
        StepBlueprint step,
        IEnumerable<EventBlueprint> events)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var eventId in HumanExitEvents(step))
        {
            result[eventId] = ResolveEventCapabilitiesForId(eventId, events);
        }

        return result;
    }

    private static List<string> ResolveEventCapabilitiesForId(string eventId, IEnumerable<EventBlueprint> events)
    {
        var match = events.FirstOrDefault(e =>
            string.Equals(e.EventId, eventId, StringComparison.OrdinalIgnoreCase));
        var declared = (match?.RequiredCapabilities ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToList();
        if (declared.Count > 0)
            return declared;

        var isHumanEvent = match != null && match.Category == EventCategory.Human;
        if (isHumanEvent || eventId.StartsWith("EVT-", StringComparison.OrdinalIgnoreCase))
            return new List<string> { $"event.publish.{eventId}" };

        return new List<string>();
    }

    private static IEnumerable<string> HumanExitEvents(StepBlueprint step)
    {
        if (step.NextSteps == null)
            yield break;
        foreach (var key in step.NextSteps.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (string.Equals(key, "Default", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "true", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return key.Trim();
        }
    }

    private static Dictionary<string, List<string>> RemapEventCapabilities(
        Dictionary<string, List<string>> source,
        WorkflowContextBindingDefinition mapping)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            var eventId = MapValue(pair.Key, mapping.EventAliases);
            result[eventId] = pair.Value
                .Select(cap => MapCapability(cap, mapping))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return result;
    }

    private static Dictionary<string, PathTravelLimit> MapPathLimits(
        IReadOnlyDictionary<string, PathTravelLimitBlueprint>? source,
        IReadOnlyDictionary<string, string>? eventAliases = null)
    {
        var result = new Dictionary<string, PathTravelLimit>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
            return result;

        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                continue;

            var key = eventAliases != null ? MapValue(pair.Key, eventAliases) : pair.Key;
            result[key] = new PathTravelLimit(pair.Value.MaxTravels, pair.Value.OnExceeded);
        }

        return result;
    }

    private static StepAutoCommitDefinition? MapAutoCommit(
        StepAutoCommitBlueprint? source,
        IReadOnlyDictionary<string, string>? eventAliases = null)
    {
        if (source == null) return null;

        var allowed = source.AllowedEvents ?? new List<string>();
        if (eventAliases != null && eventAliases.Count > 0)
        {
            allowed = allowed.Select(eventId => MapValue(eventId, eventAliases)).ToList();
        }

        return new StepAutoCommitDefinition
        {
            MinConfidence = source.MinConfidence,
            AllowedEvents = allowed
        };
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
                else if (StepActionVocabulary.IsConnectorInvocation(action.ActionType))
                {
                    target = MapCapability(target, mapping);
                }
                else
                {
                    target = MapValue(target, mapping.RoleOverrides);
                }
            }

            var connector = action.Connector ?? action.Capability;

            return new StepActionDefinition(StepActionVocabulary.NormalizeActionType(action.ActionType))
            {
                Target = target,
                Capability = string.IsNullOrWhiteSpace(connector)
                    ? connector
                    : MapCapability(connector, mapping),
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

    private static List<StepActionDefinition> MapActions(List<StepActionBlueprint>? actions)
        => actions?.Select(a => new StepActionDefinition(StepActionVocabulary.NormalizeActionType(a.ActionType))
        {
            Target = a.Target,
            Capability = StepActionVocabulary.IsConnectorInvocation(a.ActionType)
                ? StepActionVocabulary.ResolveConnectorName(a)
                : a.Connector ?? a.Capability,
            Url = a.Url,
            Method = a.Method,
            Template = a.Template,
            PayloadMapping = a.PayloadMapping,
            Condition = a.Condition,
            Headers = a.Headers,
            SignPayload = a.SignPayload,
            SecretName = a.SecretName
        }).ToList() ?? new List<StepActionDefinition>();

    public static void ApplyTemplateBusinessRoles(
        WorkflowDefinition workflow,
        WorkflowClassBlueprint blueprint,
        WorkflowContextBindingDefinition? mapping = null)
    {
        mapping ??= new WorkflowContextBindingDefinition();
        var businessRoles = ContextRoleProvisioningRules.Resolve(blueprint, mapping)
            .Select(requirement => new BusinessRoleDefinition
            {
                Name = requirement.RoleName,
                Capabilities = requirement.Capabilities.ToList(),
                ResolutionType = requirement.ResolutionType,
                MemberExpression = requirement.MemberExpression,
                StaticMembers = requirement.StaticMembers.ToList()
            })
            .ToList();
        workflow.AttachBusinessRoles(businessRoles);
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
