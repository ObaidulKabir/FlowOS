using System.Text.Json;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.ValueObjects;
using FlowOS.Events.Models;
using FlowOS.Security.Interfaces;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Services;

public sealed class WorkflowContextSimulationService : IWorkflowContextSimulationService
{
    private const int MaximumSimulationSteps = 100;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowContextBindingValidator _validator;
    private readonly IWorkflowExecutionContextService _contextService;
    private readonly WorkflowEngine _engine;
    private readonly IPluginBindingRegistryService? _pluginBindingRegistry;
    private readonly ICapabilityService? _capabilityService;

    public WorkflowContextSimulationService(
        IUnitOfWork unitOfWork,
        IWorkflowContextBindingValidator validator,
        IWorkflowExecutionContextService contextService,
        WorkflowEngine engine,
        IPluginBindingRegistryService? pluginBindingRegistry = null,
        ICapabilityService? capabilityService = null)
    {
        _unitOfWork = unitOfWork;
        _validator = validator;
        _contextService = contextService;
        _engine = engine;
        _pluginBindingRegistry = pluginBindingRegistry;
        _capabilityService = capabilityService;
    }

    public async Task<WorkflowContextSimulationResultDto> SimulateAsync(
        Guid tenantId,
        WorkflowContextSimulationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var runtime = await ResolveRuntimeAsync(tenantId, request, cancellationToken);
        var globalRoles = NormalizeRoles(request.Roles);
        var initialPrepared = _contextService.PrepareSimulationStep(
            runtime.Revision,
            runtime.SourceWorkflowClass,
            new Dictionary<string, object?>(),
            contextualEventType: null,
            request.InitialPayload,
            isInitial: true);

        var canonicalContext = ToObjectDictionary(initialPrepared.CanonicalData);
        var initialCanonicalContext = CloneDictionary(canonicalContext);
        var trace = new List<WorkflowContextSimulationTraceDto>();
        var instance = new WorkflowInstance(
            tenantId,
            runtime.WorkflowDefinition.Id,
            runtime.SourceWorkflowClass.Id,
            runtime.WorkflowDefinition.Version,
            runtime.WorkflowDefinition.StartStepId,
            initialState: runtime.StateMachineDefinition.InitialState);

        var initialStep = FindCurrentStep(runtime.WorkflowDefinition, instance);
        trace.Add(new WorkflowContextSimulationTraceDto(
            0,
            "WorkflowStarted",
            "WorkflowStarted",
            globalRoles,
            instance.CurrentStepId,
            instance.CurrentStepId,
            instance.ActiveStepIds.ToList(),
            instance.CurrentState ?? runtime.StateMachineDefinition.InitialState,
            instance.CurrentState ?? runtime.StateMachineDefinition.InitialState,
            true,
            "Initialized",
            null,
            ToSourceDictionary(request.InitialPayload),
            CloneDictionary(initialCanonicalContext),
            new Dictionary<string, object?>(),
            CloneDictionary(initialCanonicalContext),
            PlanActions(initialStep, "OnEntry"),
            PendingWork(runtime.WorkflowDefinition, instance)));

        var initialExecutionContext = await CreateExecutionContextAsync(
            tenantId,
            initialPrepared.Payload,
            globalRoles,
            cancellationToken);
        AppendAutomaticAdvances(
            trace,
            runtime,
            instance,
            initialExecutionContext,
            canonicalContext);

        var denied = false;
        var processedEvents = 0;
        var eventQueue = new Queue<WorkflowContextSimulationEventRequest>(
            request.Events ?? Array.Empty<WorkflowContextSimulationEventRequest>());
        var slaClockApplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        InjectSlaClockEvents(
            runtime.WorkflowDefinition,
            instance,
            eventQueue,
            request.AutoAdvanceTimers,
            slaClockApplied);

        while (eventQueue.Count > 0)
        {
            if (processedEvents >= request.MaxSteps) break;
            processedEvents++;
            var scenarioEvent = eventQueue.Dequeue();

            if (string.IsNullOrWhiteSpace(scenarioEvent.EventType))
                throw new ArgumentException($"Simulation event {processedEvents} must declare EventType.");

            var eventType = scenarioEvent.EventType.Trim();
            var eventRoles = scenarioEvent.Roles == null
                ? globalRoles
                : NormalizeRoles(scenarioEvent.Roles);
            var contextBefore = CloneDictionary(canonicalContext);
            var fromStep = instance.CurrentStepId;
            var fromState = instance.CurrentState ?? instance.CurrentStepId;
            var beforeStep = FindCurrentStep(runtime.WorkflowDefinition, instance, eventType);

            if (instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed)
            {
                trace.Add(DeniedTrace(
                    trace.Count,
                    eventType,
                    null,
                    eventRoles,
                    instance,
                    fromStep,
                    fromState,
                    scenarioEvent.Payload,
                    contextBefore,
                    "Cannot advance a terminated workflow.",
                    beforeStep));
                denied = true;
                break;
            }

            PreparedWorkflowSimulationContext prepared;
            try
            {
                prepared = _contextService.PrepareSimulationStep(
                    runtime.Revision,
                    runtime.SourceWorkflowClass,
                    canonicalContext,
                    eventType,
                    scenarioEvent.Payload,
                    isInitial: false);
            }
            catch (WorkflowContextPayloadException exception)
            {
                trace.Add(DeniedTrace(
                    trace.Count,
                    eventType,
                    ResolveCanonicalEvent(eventType, runtime.Revision.Definition.EventAliases),
                    eventRoles,
                    instance,
                    fromStep,
                    fromState,
                    scenarioEvent.Payload,
                    contextBefore,
                    exception.Message,
                    beforeStep));
                denied = true;
                break;
            }

            var roleFailure = IsSlaClockEvent(beforeStep, eventType)
                ? null
                : await ValidateHumanActivityAsync(runtime, beforeStep, eventType, eventRoles, tenantId, cancellationToken);
            if (roleFailure != null)
            {
                trace.Add(DeniedTrace(
                    trace.Count,
                    eventType,
                    prepared.CanonicalEventType,
                    eventRoles,
                    instance,
                    fromStep,
                    fromState,
                    scenarioEvent.Payload,
                    contextBefore,
                    roleFailure,
                    beforeStep,
                    ToObjectDictionary(prepared.Delta)));
                denied = true;
                break;
            }

            var executionContext = await CreateExecutionContextAsync(
                tenantId,
                prepared.Payload,
                eventRoles,
                cancellationToken);
            var candidate = instance.CreateTransientCopy();
            var result = _engine.Advance(
                candidate,
                runtime.WorkflowDefinition,
                new StandardEvent(tenantId, eventType),
                executionContext,
                runtime.StateMachineDefinition,
                fromState);

            if (!result.Success)
            {
                trace.Add(DeniedTrace(
                    trace.Count,
                    eventType,
                    prepared.CanonicalEventType,
                    eventRoles,
                    instance,
                    fromStep,
                    fromState,
                    scenarioEvent.Payload,
                    contextBefore,
                    result.FailureReason ?? result.Message,
                    beforeStep,
                    ToObjectDictionary(prepared.Delta)));
                denied = true;
                break;
            }

            instance = candidate;
            canonicalContext = ToObjectDictionary(prepared.CanonicalData);
            var afterStep = FindCurrentStep(runtime.WorkflowDefinition, instance);
            trace.Add(new WorkflowContextSimulationTraceDto(
                trace.Count,
                eventType,
                prepared.CanonicalEventType,
                eventRoles,
                fromStep,
                instance.CurrentStepId,
                instance.ActiveStepIds.ToList(),
                fromState,
                instance.CurrentState ?? fromState,
                true,
                result.Message,
                null,
                ToSourceDictionary(scenarioEvent.Payload),
                ToObjectDictionary(prepared.Delta),
                contextBefore,
                CloneDictionary(canonicalContext),
                PlanTransitionActions(beforeStep, afterStep, succeeded: true),
                PendingWork(runtime.WorkflowDefinition, instance)));

            AppendAutomaticAdvances(
                trace,
                runtime,
                instance,
                executionContext,
                canonicalContext);
            InjectSlaClockEvents(
                runtime.WorkflowDefinition,
                instance,
                eventQueue,
                request.AutoAdvanceTimers,
                slaClockApplied);
        }

        var stepLimitReached =
            eventQueue.Count > 0 &&
            processedEvents >= request.MaxSteps;
        var status = denied
            ? "Denied"
            : stepLimitReached
                ? "StepLimitReached"
                : instance.Status.ToString();

        return new WorkflowContextSimulationResultDto(
            runtime.Binding.Id,
            runtime.Binding.ContextType,
            runtime.Binding.Name,
            runtime.RevisionKind,
            runtime.Revision.Id,
            runtime.Revision.Revision,
            runtime.SourceWorkflowClass.Id,
            runtime.Revision.SourceWorkflowClassVersion,
            runtime.IsPersistedRuntime,
            true,
            status,
            runtime.WorkflowDefinition.StartStepId,
            instance.CurrentStepId,
            instance.ActiveStepIds.ToList(),
            runtime.StateMachineDefinition.InitialState,
            instance.CurrentState ?? runtime.StateMachineDefinition.InitialState,
            GetAvailableRoles(runtime),
            new Dictionary<string, string>(
                runtime.Revision.Definition.EventAliases,
                StringComparer.OrdinalIgnoreCase),
            BuildInitialProjection(runtime.Revision, initialPrepared),
            initialCanonicalContext,
            CloneDictionary(canonicalContext),
            trace,
            PendingWork(runtime.WorkflowDefinition, instance),
            new WorkflowContextSimulationGraphDto(
                new WorkflowContextSimulationWorkflowGraphDto(
                    runtime.WorkflowDefinition.Name,
                    runtime.WorkflowDefinition.Version,
                    runtime.WorkflowDefinition.StartStepId,
                    runtime.WorkflowDefinition.Steps),
                new WorkflowContextSimulationStateMachineGraphDto(
                    runtime.StateMachineDefinition.EntityType,
                    runtime.StateMachineDefinition.Version,
                    runtime.StateMachineDefinition.InitialState,
                    runtime.StateMachineDefinition.States.OrderBy(state => state).ToList(),
                    runtime.StateMachineDefinition.Transitions)));
    }

    private async Task<ResolvedSimulationRuntime> ResolveRuntimeAsync(
        Guid tenantId,
        WorkflowContextSimulationRequest request,
        CancellationToken cancellationToken)
    {
        var binding = request.ContextBindingId.HasValue
            ? await _unitOfWork.WorkflowContextBindings.GetByIdAsNoTrackingAsync(
                request.ContextBindingId.Value,
                tenantId,
                cancellationToken)
            : await _unitOfWork.WorkflowContextBindings.GetByContextTypeAsync(
                request.ContextType!.Trim(),
                tenantId,
                cancellationToken);
        if (binding == null) throw new KeyNotFoundException("Workflow context binding was not found.");

        var revisionKind = request.Revision.Trim().ToLowerInvariant();
        var revisionId = revisionKind == "draft"
            ? binding.DraftRevisionId
            : binding.ActiveRevisionId;
        if (!revisionId.HasValue)
            throw new KeyNotFoundException($"The binding has no {revisionKind} revision.");

        var revision = await _unitOfWork.WorkflowContextBindings
            .GetRevisionByIdAsNoTrackingAsync(revisionId.Value, cancellationToken);
        if (revision == null || revision.BindingId != binding.Id)
            throw new KeyNotFoundException($"The binding's {revisionKind} revision was not found.");

        var source = await _unitOfWork.WorkflowClasses
            .GetByIdAsNoTrackingAsync(revision.SourceWorkflowClassId, cancellationToken)
            ?? throw new KeyNotFoundException("The binding source workflow template was not found.");

        if (revisionKind == "draft")
        {
            if (revision.Status != WorkflowContextBindingRevisionStatus.Draft)
                throw new InvalidOperationException("The selected revision is not a draft.");
            var validation = await _validator.ValidateAsync(
                binding,
                revision,
                WorkflowContextBindingValidationOptions.Simulation,
                cancellationToken);
            if (!validation.IsValid)
                throw new WorkflowContextBindingValidationException(validation);

            var package = WorkflowClassCompiler.MapToContextRuntimePackage(source, binding, revision);
            return new ResolvedSimulationRuntime(
                binding,
                revision,
                source,
                package.WorkflowDefinition,
                package.StateMachineDefinition,
                "draft",
                false);
        }

        if (revision.Status != WorkflowContextBindingRevisionStatus.Active ||
            !revision.WorkflowDefinitionId.HasValue ||
            !revision.StateMachineDefinitionId.HasValue)
        {
            throw new InvalidOperationException("The active context-binding revision is incomplete.");
        }

        var workflowDefinition = await _unitOfWork.WorkflowDefinitions
            .GetByIdAsNoTrackingAsync(revision.WorkflowDefinitionId.Value, cancellationToken);
        var stateMachineDefinition = await _unitOfWork.StateMachines
            .GetByIdAsNoTrackingAsync(revision.StateMachineDefinitionId.Value, cancellationToken);
        if (workflowDefinition == null ||
            stateMachineDefinition == null ||
            workflowDefinition.TenantId != tenantId ||
            stateMachineDefinition.TenantId != tenantId ||
            workflowDefinition.ContextBindingRevisionId != revision.Id)
        {
            throw new InvalidOperationException("The active context binding has no valid pinned runtime package.");
        }

        return new ResolvedSimulationRuntime(
            binding,
            revision,
            source,
            workflowDefinition,
            stateMachineDefinition,
            "active",
            true);
    }

    private async Task<FlowOS.StateMachines.Models.ExecutionContext> CreateExecutionContextAsync(
        Guid tenantId,
        Dictionary<string, object> payload,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken)
    {
        var context = new FlowOS.StateMachines.Models.ExecutionContext
        {
            Payload = payload,
            Metadata = new Dictionary<string, object>
            {
                ["Roles"] = roles.ToList()
            }
        };

        if (_pluginBindingRegistry != null)
        {
            var bindings = await _pluginBindingRegistry.ResolveBindingsAsync(
                tenantId,
                PluginBindingTypes.Decision,
                cancellationToken);
            if (bindings.Count > 0)
            {
                context.DecisionProviderBindings = bindings;
            }
        }

        return context;
    }

    private void AppendAutomaticAdvances(
        List<WorkflowContextSimulationTraceDto> trace,
        ResolvedSimulationRuntime runtime,
        WorkflowInstance instance,
        FlowOS.StateMachines.Models.ExecutionContext context,
        IReadOnlyDictionary<string, object?> canonicalContext)
    {
        var automatic = WorkflowAutoAdvanceRunner.Run(
            _engine,
            instance,
            runtime.WorkflowDefinition,
            runtime.Binding.TenantId,
            context,
            runtime.StateMachineDefinition);

        foreach (var item in automatic)
        {
            var beforeStep = FindStep(runtime.WorkflowDefinition, item.FromStepId);
            var afterStep = FindStep(runtime.WorkflowDefinition, item.ToStepId);
            trace.Add(new WorkflowContextSimulationTraceDto(
                trace.Count,
                item.EventType,
                item.EventType,
                GetRoles(context),
                item.FromStepId,
                item.ToStepId,
                item.ActiveStepIds,
                item.FromState,
                item.ToState,
                item.IsAllowed,
                item.IsAllowed ? "Automatically advanced" : "Automatic advance denied",
                item.IsAllowed ? null : item.Message,
                new Dictionary<string, object?>(),
                new Dictionary<string, object?>(),
                CloneDictionary(canonicalContext),
                CloneDictionary(canonicalContext),
                PlanTransitionActions(beforeStep, afterStep, item.IsAllowed),
                item.IsAllowed
                    ? PendingWork(runtime.WorkflowDefinition, instance)
                    : Array.Empty<WorkflowContextSimulationPendingWorkDto>()));
        }
    }

    private static WorkflowContextSimulationTraceDto DeniedTrace(
        int index,
        string eventType,
        string? canonicalEventType,
        IReadOnlyList<string> roles,
        WorkflowInstance instance,
        string fromStep,
        string fromState,
        object? sourcePayload,
        IReadOnlyDictionary<string, object?> contextBefore,
        string reason,
        WorkflowStepDefinition? currentStep,
        IReadOnlyDictionary<string, object?>? delta = null)
        => new(
            index,
            eventType,
            canonicalEventType,
            roles,
            fromStep,
            instance.CurrentStepId,
            instance.ActiveStepIds.ToList(),
            fromState,
            instance.CurrentState ?? fromState,
            false,
            "Denied",
            reason,
            ToSourceDictionary(sourcePayload),
            delta ?? new Dictionary<string, object?>(),
            CloneDictionary(contextBefore),
            CloneDictionary(contextBefore),
            PlanActions(currentStep, "OnFailure"),
            PendingWorkForStep(currentStep));

    private static bool IsSlaClockEvent(WorkflowStepDefinition? step, string eventType) =>
        SlaSimulationClock.IsReminderEvent(step?.Sla, eventType) ||
        SlaSimulationClock.IsTimeoutEvent(step?.Sla, eventType);

    private async Task<string?> ValidateHumanActivityAsync(
        ResolvedSimulationRuntime runtime,
        WorkflowStepDefinition? step,
        string eventType,
        IReadOnlyList<string> roles,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (ActivityAuthorization.IsAdmin(roles))
            return null;

        var required = ActivityAuthorization.ResolveRequiredCapabilities(step, eventType);
        if (required.Count == 0)
            return null;

        var callerCaps = await ResolveSimulatedCapabilitiesAsync(runtime, roles, tenantId, cancellationToken);
        return ActivityAuthorization.HasGrant(callerCaps, required)
            ? null
            : $"Simulated role lacks a required capability for '{eventType}'. Required one of: {string.Join(", ", required)}.";
    }

    private async Task<HashSet<string>> ResolveSimulatedCapabilitiesAsync(
        ResolvedSimulationRuntime runtime,
        IReadOnlyList<string> roles,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var caps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_capabilityService != null && roles.Count > 0)
        {
            var tenantCaps = await _capabilityService.GetCapabilitiesAsync(tenantId, roles);
            foreach (var cap in tenantCaps)
                caps.Add(cap);
        }

        foreach (var roleName in roles)
        {
            foreach (var cap in CatalogCapabilitiesForRole(runtime, roleName))
                caps.Add(cap);
        }

        return caps;
    }

    private static IEnumerable<string> CatalogCapabilitiesForRole(
        ResolvedSimulationRuntime runtime,
        string roleName)
    {
        foreach (var templateRole in runtime.SourceWorkflowClass.Definition.Roles)
        {
            var effective = TryGetValue(
                runtime.Revision.Definition.RoleOverrides,
                templateRole.Name,
                out var mapped)
                ? mapped
                : templateRole.Name;
            if (!string.Equals(effective, roleName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(templateRole.Name, roleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var cap in templateRole.GrantedCapabilities ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(cap))
                    continue;
                yield return ActivityAuthorization.MapCapability(
                    cap.Trim(),
                    runtime.Revision.Definition.CapabilityOverrides,
                    runtime.Revision.Definition.EventAliases);
            }
        }
    }

    private static IReadOnlyList<WorkflowContextSimulationProjectionItemDto> BuildInitialProjection(
        WorkflowContextBindingRevision revision,
        PreparedWorkflowSimulationContext prepared)
    {
        var result = new List<WorkflowContextSimulationProjectionItemDto>();
        foreach (var mapping in revision.Definition.InputMapping)
        {
            var resolved = TryGetValue(prepared.Delta, mapping.Key, out var value);
            result.Add(new WorkflowContextSimulationProjectionItemDto(
                mapping.Key,
                mapping.Value,
                resolved ? ConvertJson(value) : null,
                "InputMapping",
                resolved));
        }

        foreach (var parameter in revision.Definition.ConditionParameters)
        {
            result.Add(new WorkflowContextSimulationProjectionItemDto(
                parameter.Key,
                null,
                ConvertJson(parameter.Value),
                "ConditionParameter",
                true));
        }

        return result
            .OrderBy(item => item.CanonicalField, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetAvailableRoles(ResolvedSimulationRuntime runtime)
        => runtime.WorkflowDefinition.Steps
            .SelectMany(step => step.AllowedRoles)
            .Concat(runtime.StateMachineDefinition.Transitions
                .SelectMany(transition => transition.Constraints
                    .Where(item => string.Equals(item.Key, "Role", StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Value)))
            .Concat(runtime.Revision.Definition.RoleOverrides.Values)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<WorkflowContextSimulationActionDto> PlanTransitionActions(
        WorkflowStepDefinition? beforeStep,
        WorkflowStepDefinition? afterStep,
        bool succeeded)
    {
        if (!succeeded) return PlanActions(beforeStep, "OnFailure");
        return PlanActions(beforeStep, "OnExit")
            .Concat(PlanActions(afterStep, "OnEntry"))
            .ToList();
    }

    private static IReadOnlyList<WorkflowContextSimulationActionDto> PlanActions(
        WorkflowStepDefinition? step,
        string phase)
    {
        if (step == null) return Array.Empty<WorkflowContextSimulationActionDto>();
        var actions = phase switch
        {
            "OnEntry" => step.OnEntry,
            "OnExit" => step.OnExit,
            "OnFailure" => step.OnFailure,
            _ => new List<StepActionDefinition>()
        };
        return actions.Select(action => new WorkflowContextSimulationActionDto(
            step.StepId,
            phase,
            action.ActionType,
            action.Target,
            action.Capability,
            action.Condition)).ToList();
    }

    private static IReadOnlyList<WorkflowContextSimulationPendingWorkDto> PendingWork(
        WorkflowDefinition definition,
        WorkflowInstance instance)
    {
        var activeIds = instance.ActiveStepIds.Count > 0
            ? instance.ActiveStepIds
            : new List<string> { instance.CurrentStepId };
        return activeIds
            .Select(id => FindStep(definition, id))
            .Where(step => step != null)
            .SelectMany(PendingWorkForStep)
            .ToList();
    }

    private static IReadOnlyList<WorkflowContextSimulationPendingWorkDto> PendingWorkForStep(
        WorkflowStepDefinition? step)
    {
        if (step == null) return Array.Empty<WorkflowContextSimulationPendingWorkDto>();
        return step.StepType switch
        {
            WorkflowStepType.HumanTask => DescribeHumanTaskPendingWork(step),
            WorkflowStepType.Timer => new[]
            {
                new WorkflowContextSimulationPendingWorkDto(
                    "Timer",
                    step.StepId,
                    step.NextSteps.Keys.FirstOrDefault(),
                    step.Sla == null
                        ? "Timer is planned but not scheduled in simulation."
                        : $"Timer/SLA {step.Sla.Duration} fires in simulation when autoAdvanceTimers is true.")
            },
            WorkflowStepType.SubWorkflow => new[]
            {
                new WorkflowContextSimulationPendingWorkDto(
                    "SubWorkflow",
                    step.StepId,
                    "WorkflowCompleted",
                    $"Child workflow '{step.SubWorkflow?.WorkflowName ?? step.SubWorkflow?.WorkflowDefinitionId?.ToString() ?? "unresolved"}' would be started.")
            },
            _ => Array.Empty<WorkflowContextSimulationPendingWorkDto>()
        };
    }

    private static WorkflowContextSimulationPendingWorkDto[] DescribeHumanTaskPendingWork(
        WorkflowStepDefinition step)
    {
        var waiting = step.AllowedRoles.Count == 0
            ? "Waiting for human completion."
            : $"Waiting for role: {string.Join(" or ", step.AllowedRoles)}.";
        if (step.Sla == null)
        {
            return new[]
            {
                new WorkflowContextSimulationPendingWorkDto(
                    "HumanTask",
                    step.StepId,
                    step.NextSteps.Keys.FirstOrDefault(),
                    waiting)
            };
        }

        var reminders = step.Sla.Reminders.Count == 0
            ? "none"
            : string.Join(", ", step.Sla.Reminders.Select(r => $"{r.TriggerEvent}@{r.Duration}"));
        var items = new List<WorkflowContextSimulationPendingWorkDto>
        {
            new(
                "HumanTask",
                step.StepId,
                step.NextSteps.Keys.FirstOrDefault(),
                waiting +
                $" SLA {step.Sla.Duration} reminders [{reminders}] timeout {step.Sla.TimeoutEvent}. " +
                "Simulation fires those clock events when autoAdvanceTimers is true or a completing nextSteps event is queued.")
        };
        foreach (var reminder in step.Sla.Reminders)
        {
            items.Add(new WorkflowContextSimulationPendingWorkDto(
                "SlaReminder",
                step.StepId,
                reminder.TriggerEvent,
                $"Reminder at {reminder.Duration}."));
        }

        if (!string.IsNullOrWhiteSpace(step.Sla.TimeoutEvent))
        {
            items.Add(new WorkflowContextSimulationPendingWorkDto(
                "SlaTimeout",
                step.StepId,
                step.Sla.TimeoutEvent,
                $"Timeout after {step.Sla.Duration}."));
        }

        return items.ToArray();
    }

    private static void InjectSlaClockEvents(
        WorkflowDefinition definition,
        WorkflowInstance instance,
        Queue<WorkflowContextSimulationEventRequest> eventQueue,
        bool autoAdvanceTimers,
        ISet<string> slaClockApplied)
    {
        var step = FindCurrentStep(definition, instance);
        if (step?.Sla == null) return;
        if (step.StepType is WorkflowStepType.Decision or WorkflowStepType.Fork or WorkflowStepType.Join)
            return;
        if (step.StepType != WorkflowStepType.HumanTask &&
            step.NextSteps.ContainsKey("Default"))
        {
            return;
        }

        if (!slaClockApplied.Add(step.StepId)) return;

        var queuedTypes = eventQueue.Select(e => e.EventType).ToList();
        var completing = queuedTypes.Any(evt =>
            !SlaSimulationClock.IsReminderEvent(step.Sla, evt) &&
            step.NextSteps.ContainsKey(evt));
        var selected = SlaSimulationClock.SelectForSimulation(
            SlaSimulationClock.Plan(step.Sla),
            completing,
            autoAdvanceTimers,
            queuedTypes);
        if (selected.Count == 0) return;

        var rest = eventQueue.ToList();
        eventQueue.Clear();
        foreach (var job in selected)
            eventQueue.Enqueue(new WorkflowContextSimulationEventRequest(job.EventType));
        foreach (var remaining in rest)
            eventQueue.Enqueue(remaining);
    }

    private static WorkflowStepDefinition? FindCurrentStep(
        WorkflowDefinition definition,
        WorkflowInstance instance,
        string? eventType = null)
    {
        if (!string.IsNullOrWhiteSpace(eventType) && instance.ActiveStepIds.Count > 1)
        {
            var branch = definition.Steps.FirstOrDefault(step =>
                instance.ActiveStepIds.Contains(step.StepId) &&
                step.NextSteps.ContainsKey(eventType));
            if (branch != null) return branch;
        }
        return FindStep(definition, instance.CurrentStepId) ??
               instance.ActiveStepIds.Select(id => FindStep(definition, id)).FirstOrDefault(step => step != null);
    }

    private static WorkflowStepDefinition? FindStep(WorkflowDefinition definition, string stepId)
        => definition.Steps.FirstOrDefault(step =>
            string.Equals(step.StepId, stepId, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> NormalizeRoles(IReadOnlyList<string>? roles)
        => (roles ?? Array.Empty<string>())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> GetRoles(FlowOS.StateMachines.Models.ExecutionContext context)
    {
        if (!context.Metadata.TryGetValue("Roles", out var roles)) return Array.Empty<string>();
        return roles is IEnumerable<string> values ? values.ToList() : Array.Empty<string>();
    }

    private static Dictionary<string, object?> ToObjectDictionary(
        IReadOnlyDictionary<string, JsonElement> values)
        => values.ToDictionary(
            item => item.Key,
            item => ConvertJson(item.Value),
            StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, object?> ToSourceDictionary(object? value)
    {
        if (value == null) return new Dictionary<string, object?>();
        var element = value is JsonElement jsonElement
            ? jsonElement
            : JsonSerializer.SerializeToElement(value);
        if (element.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?> { ["value"] = ConvertJson(element) };
        return element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => ConvertJson(property.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static object? ConvertJson(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(
                property => property.Name,
                property => ConvertJson(property.Value),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => value.EnumerateArray().Select(ConvertJson).ToList(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

    private static Dictionary<string, object?> CloneDictionary(
        IReadOnlyDictionary<string, object?> values)
        => values.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);

    private static string ResolveCanonicalEvent(
        string contextualEvent,
        IReadOnlyDictionary<string, string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (string.Equals(alias.Value, contextualEvent, StringComparison.OrdinalIgnoreCase))
                return alias.Key;
        }
        return contextualEvent;
    }

    private static bool TryGetValue<T>(
        IReadOnlyDictionary<string, T> values,
        string key,
        out T value)
    {
        foreach (var item in values)
        {
            if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    private static void ValidateRequest(WorkflowContextSimulationRequest request)
    {
        if (request.ContextBindingId.HasValue == !string.IsNullOrWhiteSpace(request.ContextType))
            throw new ArgumentException("Specify exactly one of ContextBindingId or ContextType.");
        if (!string.Equals(request.Revision, "draft", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Revision, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Revision must be either 'draft' or 'active'.");
        }
        if (request.MaxSteps is < 1 or > MaximumSimulationSteps)
            throw new ArgumentOutOfRangeException(nameof(request.MaxSteps), $"MaxSteps must be between 1 and {MaximumSimulationSteps}.");
        if ((request.Events?.Count ?? 0) > MaximumSimulationSteps)
            throw new ArgumentException($"A simulation accepts at most {MaximumSimulationSteps} events.");
    }

    private sealed record ResolvedSimulationRuntime(
        WorkflowContextBinding Binding,
        WorkflowContextBindingRevision Revision,
        WorkflowClass SourceWorkflowClass,
        WorkflowDefinition WorkflowDefinition,
        StateMachineDefinition StateMachineDefinition,
        string RevisionKind,
        bool IsPersistedRuntime);
}
