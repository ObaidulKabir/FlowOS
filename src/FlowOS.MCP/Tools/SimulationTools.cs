using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Application.Queries.Governance;
using FlowOS.Domain.Blueprints;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.StateMachines.Engine;
using MediatR;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class SimulationTools
{
    private readonly IMediator _mediator;
    private readonly FlowOS.Core.Common.Interfaces.ICompensationPlannerService? _compensationPlanner;

    public SimulationTools(
        IMediator mediator,
        FlowOS.Core.Common.Interfaces.ICompensationPlannerService? compensationPlanner = null)
    {
        _mediator = mediator;
        _compensationPlanner = compensationPlanner;
    }

    public record SimulationRunResult(
        string Workflow,
        string Status,
        string InitialState,
        string FinalState,
        string InitialStepId,
        string CurrentStepId,
        int TotalStepsExecuted,
        string SimulatedRole,
        object? PendingHumanTask,
        object? PendingSubWorkflow,
        List<object> DecisionsEvaluated,
        List<object> StateTransitions,
        List<object> ActionsTriggered,
        List<object> ExecutionTrace,
        Dictionary<string, object> Payload,
        List<object> SubWorkflowsExecuted,
        object? PendingTimer = null
    );

    public async Task<CallToolResult> SimulateWorkflowClass(JObject args)
    {
        try
        {
            WorkflowClassBlueprint? blueprint = null;
            string sourceWorkflowName = "InlineBlueprint";
            Guid tenantId = Guid.Empty;

            // 1. Resolve blueprint either from inline object or via draft/published ID
            var inlineBlueprintToken = args["blueprint"] as JObject;
            if (inlineBlueprintToken != null)
            {
                blueprint = inlineBlueprintToken.ToObject<WorkflowClassBlueprint>();
                if (blueprint == null || blueprint.Workflow?.Steps == null || blueprint.Workflow.Steps.Count == 0)
                {
                    return McpToolResults.Fail("MCP-ARG-001", "Provided inline blueprint is invalid or contains no workflow steps.");
                }
                var bpName = args["name"]?.ToString() ?? inlineBlueprintToken["name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(bpName))
                {
                    sourceWorkflowName = bpName;
                }
            }
            else
            {
                var idStr = args["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(idStr) || !Guid.TryParse(idStr, out var id))
                {
                    return McpToolResults.Fail("MCP-ARG-001", "Either 'blueprint' object or a valid UUID 'id' must be provided.");
                }

                try
                {
                    tenantId = McpTenantResolver.ResolveRequired(args);
                }
                catch (McpToolException ex)
                {
                    return McpToolResults.Fail(ex.Code, ex.Message);
                }

                WorkflowClassResponseDto? workflowClass;
                try
                {
                    workflowClass = await _mediator.Send(new GetWorkflowClassByIdQuery(tenantId, id));
                }
                catch (UnauthorizedAccessException)
                {
                    return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found.");
                }
                catch (KeyNotFoundException)
                {
                    return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found.");
                }

                if (workflowClass == null || workflowClass.Definition == null)
                {
                    return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found or has empty definition.");
                }

                blueprint = workflowClass.Definition;
                sourceWorkflowName = $"{workflowClass.Name} v{workflowClass.Version}";
            }

            if (blueprint.Workflow?.Steps == null || blueprint.Workflow.Steps.Count == 0)
            {
                return McpToolResults.Fail("MCP-VALIDATION", "Workflow must contain at least one step to simulate.");
            }

            var payload = ToPayloadDictionary(args["payload"] as JObject);
            var simulatedRole = args["role"]?.ToString()?.Trim();
            if (string.IsNullOrEmpty(simulatedRole))
                simulatedRole = "User";

            var eventsQueue = ParseEventsQueue(args["events"]);
            var childEventsQueue = ParseEventsQueue(args["childEvents"]);

            int maxSteps = args["maxSteps"]?.Value<int>() ?? 25;
            if (maxSteps < 1) maxSteps = 1;
            if (maxSteps > 100) maxSteps = 100;

            var simulateFailureAtStep = args["simulateFailureAtStep"]?.ToString()?.Trim();
            var inlineSubWorkflows = ParseInlineSubWorkflows(args);
            bool autoCompleteSubWorkflows = args["autoCompleteSubWorkflows"]?.Value<bool>() ?? false;
            bool autoAdvanceTimers = args["autoAdvanceTimers"]?.Value<bool>() ?? false;

            var runResult = await ExecuteSimulationRunAsync(
                blueprint,
                sourceWorkflowName,
                payload,
                simulatedRole,
                eventsQueue,
                maxSteps,
                simulateFailureAtStep,
                tenantId,
                inlineSubWorkflows,
                autoCompleteSubWorkflows,
                recursionDepth: 0,
                childEventsQueue: childEventsQueue,
                autoAdvanceTimers: autoAdvanceTimers
            );

            return McpToolResults.Success(new
            {
                workflow = runResult.Workflow,
                status = runResult.Status,
                initialState = runResult.InitialState,
                finalState = runResult.FinalState,
                initialStepId = runResult.InitialStepId,
                currentStepId = runResult.CurrentStepId,
                totalStepsExecuted = runResult.TotalStepsExecuted,
                simulatedRole = runResult.SimulatedRole,
                pendingHumanTask = runResult.PendingHumanTask,
                pendingSubWorkflow = runResult.PendingSubWorkflow,
                pendingTimer = runResult.PendingTimer,
                decisionsEvaluated = runResult.DecisionsEvaluated,
                stateTransitions = runResult.StateTransitions,
                actionsTriggered = runResult.ActionsTriggered,
                executionTrace = runResult.ExecutionTrace,
                payload = runResult.Payload,
                subworkflowsExecuted = runResult.SubWorkflowsExecuted
            });
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Simulation execution failed: {ex.Message}");
        }
    }

    public async Task<CallToolResult> SimulateSubWorkflow(JObject args)
    {
        try
        {
            WorkflowClassBlueprint? parentBlueprint = null;
            string parentWorkflowName = "ParentWorkflow";
            Guid tenantId = Guid.Empty;

            var parentBpToken = (args["parentBlueprint"] as JObject) ?? (args["blueprint"] as JObject);
            if (parentBpToken != null)
            {
                parentBlueprint = parentBpToken.ToObject<WorkflowClassBlueprint>();
                if (parentBlueprint == null || parentBlueprint.Workflow?.Steps == null || parentBlueprint.Workflow.Steps.Count == 0)
                {
                    return McpToolResults.Fail("MCP-ARG-001", "Provided parent blueprint is invalid or contains no workflow steps.");
                }
                var parentName = args["name"]?.ToString() ?? parentBpToken["name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(parentName))
                {
                    parentWorkflowName = parentName;
                }
            }
            else
            {
                var idStr = (args["parentWorkflowClassId"] ?? args["id"])?.ToString();
                if (string.IsNullOrWhiteSpace(idStr) || !Guid.TryParse(idStr, out var id))
                {
                    return McpToolResults.Fail("MCP-ARG-001", "Either 'parentBlueprint' object or a valid UUID 'parentWorkflowClassId' must be provided.");
                }

                try
                {
                    tenantId = McpTenantResolver.ResolveRequired(args);
                }
                catch (McpToolException ex)
                {
                    return McpToolResults.Fail(ex.Code, ex.Message);
                }

                WorkflowClassResponseDto? workflowClass;
                try
                {
                    workflowClass = await _mediator.Send(new GetWorkflowClassByIdQuery(tenantId, id));
                }
                catch (Exception)
                {
                    return McpToolResults.Fail("MCP-NOTFOUND-001", "Parent WorkflowClass not found.");
                }

                if (workflowClass == null || workflowClass.Definition == null)
                {
                    return McpToolResults.Fail("MCP-NOTFOUND-001", "Parent WorkflowClass not found or has empty definition.");
                }

                parentBlueprint = workflowClass.Definition;
                parentWorkflowName = $"{workflowClass.Name} v{workflowClass.Version}";
            }

            // Identify target SubWorkflow step
            var explicitStepId = args["stepId"]?.ToString()?.Trim();
            StepBlueprint? subWorkflowStep = null;
            if (!string.IsNullOrEmpty(explicitStepId))
            {
                subWorkflowStep = parentBlueprint.Workflow.Steps.FirstOrDefault(s =>
                    string.Equals(s.StepId, explicitStepId, StringComparison.OrdinalIgnoreCase));
                if (subWorkflowStep == null)
                {
                    return McpToolResults.Fail("MCP-ARG-001", $"Step '{explicitStepId}' not found in parent workflow blueprint.");
                }
                if (!string.Equals(subWorkflowStep.StepType, "SubWorkflow", StringComparison.OrdinalIgnoreCase))
                {
                    return McpToolResults.Fail("MCP-ARG-001", $"Step '{explicitStepId}' has StepType '{subWorkflowStep.StepType}', expected 'SubWorkflow'.");
                }
            }
            else
            {
                subWorkflowStep = parentBlueprint.Workflow.Steps.FirstOrDefault(s =>
                    string.Equals(s.StepType, "SubWorkflow", StringComparison.OrdinalIgnoreCase));
                if (subWorkflowStep == null)
                {
                    return McpToolResults.Fail("MCP-ARG-001", "No step with StepType 'SubWorkflow' found in parent workflow blueprint.");
                }
            }

            var inlineSubWorkflows = ParseInlineSubWorkflows(args);

            var childBpToken = args["childBlueprint"] as JObject;
            WorkflowClassBlueprint? childBlueprint = null;
            string childWorkflowName = subWorkflowStep.SubWorkflow?.WorkflowName ?? "ChildWorkflow";
            if (childBpToken != null)
            {
                childBlueprint = childBpToken.ToObject<WorkflowClassBlueprint>();
                if (childBlueprint != null)
                {
                    var childName = childBpToken["name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(childName))
                    {
                        childWorkflowName = childName;
                    }
                    inlineSubWorkflows[childWorkflowName] = childBlueprint;
                    inlineSubWorkflows[subWorkflowStep.StepId] = childBlueprint;
                    if (!string.IsNullOrWhiteSpace(subWorkflowStep.SubWorkflow?.WorkflowName))
                    {
                        inlineSubWorkflows[subWorkflowStep.SubWorkflow.WorkflowName] = childBlueprint;
                    }
                }
            }

            var payload = ToPayloadDictionary((args["payload"] as JObject) ?? (args["parentPayload"] as JObject));
            var simulatedRole = args["role"]?.ToString()?.Trim();
            if (string.IsNullOrEmpty(simulatedRole))
                simulatedRole = "User";

            var eventsQueue = ParseEventsQueue(args["events"]);
            var childEventsQueue = ParseEventsQueue(args["childEvents"]);

            int maxSteps = args["maxSteps"]?.Value<int>() ?? 25;
            if (maxSteps < 1) maxSteps = 1;
            if (maxSteps > 100) maxSteps = 100;

            bool autoAdvanceTimers = args["autoAdvanceTimers"]?.Value<bool>() ?? false;

            var runResult = await ExecuteSimulationRunAsync(
                parentBlueprint,
                parentWorkflowName,
                payload,
                simulatedRole,
                eventsQueue,
                maxSteps,
                simulateFailureAtStep: null,
                tenantId,
                inlineSubWorkflows,
                autoCompleteSubWorkflows: false,
                recursionDepth: 0,
                childEventsQueue: childEventsQueue,
                autoAdvanceTimers: autoAdvanceTimers
            );

            return McpToolResults.Success(new
            {
                parentWorkflow = parentWorkflowName,
                subWorkflowStepId = subWorkflowStep.StepId,
                childWorkflow = childWorkflowName,
                status = runResult.Status,
                initialParentState = runResult.InitialState,
                finalParentState = runResult.FinalState,
                inputMapping = subWorkflowStep.SubWorkflow?.InputMapping ?? new Dictionary<string, string>(),
                outputMapping = subWorkflowStep.SubWorkflow?.OutputMapping ?? new Dictionary<string, string>(),
                updatedParentPayload = runResult.Payload,
                subworkflowsExecuted = runResult.SubWorkflowsExecuted,
                pendingSubWorkflow = runResult.PendingSubWorkflow,
                pendingTimer = runResult.PendingTimer,
                totalParentStepsExecuted = runResult.TotalStepsExecuted,
                parentExecutionTrace = runResult.ExecutionTrace,
                parentStateTransitions = runResult.StateTransitions
            });
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Subworkflow simulation failed: {ex.Message}");
        }
    }

    private async Task<SimulationRunResult> ExecuteSimulationRunAsync(
        WorkflowClassBlueprint blueprint,
        string sourceWorkflowName,
        Dictionary<string, object> payload,
        string simulatedRole,
        Queue<string> eventsQueue,
        int maxSteps,
        string? simulateFailureAtStep,
        Guid tenantId,
        Dictionary<string, WorkflowClassBlueprint>? inlineSubWorkflows,
        bool autoCompleteSubWorkflows,
        int recursionDepth,
        Queue<string>? childEventsQueue = null,
        bool autoAdvanceTimers = false)
    {
        var startStepId = !string.IsNullOrWhiteSpace(blueprint.Workflow?.StartStepId)
            ? blueprint.Workflow.StartStepId
            : (blueprint.Workflow?.Steps.FirstOrDefault()?.StepId ?? "Start");

        var currentStepId = startStepId;
        var currentState = !string.IsNullOrWhiteSpace(blueprint.StateMachine?.InitialState)
            ? blueprint.StateMachine.InitialState
            : "Initial";

        var initialState = currentState;
        int totalStepsExecuted = 0;
        string status = "Running";

        var executionTrace = new List<object>();
        var decisionsEvaluated = new List<object>();
        var stateTransitions = new List<object>();
        var actionsTriggered = new List<object>();
        var subworkflowsExecuted = new List<object>();
        object? pendingHumanTask = null;
        object? pendingSubWorkflow = null;
        object? pendingTimer = null;

        var initialStepObj = blueprint.Workflow?.Steps.FirstOrDefault(s =>
            string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
        if (initialStepObj != null)
        {
            EvaluateAndRecordActions(initialStepObj.OnEntry, "OnEntry", initialStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
        }

        while (totalStepsExecuted < maxSteps)
        {
            if (string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
            {
                status = "Completed";
                executionTrace.Add(new
                {
                    stepNumber = totalStepsExecuted + 1,
                    stepId = "END",
                    stepType = "End",
                    action = "Workflow reached terminal END step. Simulation completed successfully.",
                    state = currentState
                });
                break;
            }

            var step = blueprint.Workflow?.Steps.FirstOrDefault(s =>
                string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));

            if (step == null)
            {
                status = "Faulted";
                executionTrace.Add(new
                {
                    stepNumber = totalStepsExecuted + 1,
                    stepId = currentStepId,
                    stepType = "Unknown",
                    action = $"Target step '{currentStepId}' not found in workflow blueprint.",
                    state = currentState
                });
                break;
            }

            var stepType = !string.IsNullOrWhiteSpace(step.StepType) ? step.StepType : "Command";
            var stepTypeLower = stepType.ToLowerInvariant();

            // --- SIMULATE FAILURE / SAGA ROLLBACK CHECK ---
            if (!string.IsNullOrEmpty(simulateFailureAtStep) &&
                string.Equals(step.StepId, simulateFailureAtStep, StringComparison.OrdinalIgnoreCase))
            {
                status = "Faulted";
                totalStepsExecuted++;
                executionTrace.Add(new
                {
                    stepNumber = totalStepsExecuted,
                    stepId = step.StepId,
                    stepType = stepType,
                    action = $"[Saga Failure Injected] Step '{step.StepId}' encountered a simulated failure. Executing OnFailure compensating actions...",
                    state = currentState
                });

                EvaluateAndRecordActions(step.OnFailure, "OnFailure", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                break;
            }

            if (stepTypeLower.Contains("end"))
            {
                status = "Completed";
                executionTrace.Add(new
                {
                    stepNumber = totalStepsExecuted + 1,
                    stepId = step.StepId,
                    stepType = "End",
                    action = $"Workflow reached end step '{step.StepId}'. Simulation completed successfully.",
                    state = currentState
                });
                break;
            }

            // --- DECISION STEP ---
            if (stepTypeLower.Contains("decision") || stepTypeLower.Contains("choice"))
            {
                string? winningTarget = null;
                string? winningExpr = null;

                var conditions = step.Conditions ?? new Dictionary<string, string>();
                string? defaultTarget = null;

                foreach (var kvp in conditions)
                {
                    var expr = kvp.Key;
                    var target = kvp.Value;

                    if (string.Equals(expr.Trim(), "default", StringComparison.OrdinalIgnoreCase))
                    {
                        defaultTarget = target;
                        continue;
                    }

                    bool matched = EvaluateExpressionSafely(expr, payload);
                    decisionsEvaluated.Add(new
                    {
                        stepId = step.StepId,
                        expression = expr,
                        matched,
                        target
                    });

                    if (matched && winningTarget == null)
                    {
                        winningTarget = target;
                        winningExpr = expr;
                    }
                }

                if (winningTarget == null && defaultTarget != null)
                {
                    winningTarget = defaultTarget;
                    winningExpr = "Default";
                    decisionsEvaluated.Add(new
                    {
                        stepId = step.StepId,
                        expression = "Default",
                        matched = true,
                        target = defaultTarget
                    });
                }

                if (winningTarget != null)
                {
                    totalStepsExecuted++;
                    executionTrace.Add(new
                    {
                        stepNumber = totalStepsExecuted,
                        stepId = step.StepId,
                        stepType = "Decision",
                        action = $"Condition '{winningExpr}' evaluated to TRUE => Advanced to '{winningTarget}'.",
                        state = currentState
                    });

                    EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                    currentStepId = winningTarget;
                    if (!string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
                    {
                        var targetStepObj = blueprint.Workflow?.Steps.FirstOrDefault(s =>
                            string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
                        if (targetStepObj != null)
                        {
                            EvaluateAndRecordActions(targetStepObj.OnEntry, "OnEntry", targetStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                        }
                    }
                    continue;
                }
                else
                {
                    status = "StuckAtDecision";
                    executionTrace.Add(new
                    {
                        stepNumber = totalStepsExecuted + 1,
                        stepId = step.StepId,
                        stepType = "Decision",
                        action = $"Decision step '{step.StepId}' evaluated all conditions to FALSE and no 'Default' path was specified.",
                        state = currentState
                    });
                    break;
                }
            }

            // --- FORK STEP ---
            if (stepTypeLower.Contains("fork"))
            {
                var forkBranches = (step.Branches != null && step.Branches.Any())
                    ? step.Branches
                    : (step.NextSteps != null ? step.NextSteps.Values.Distinct().ToList() : new List<string>());

                totalStepsExecuted++;
                executionTrace.Add(new
                {
                    stepNumber = totalStepsExecuted,
                    stepId = step.StepId,
                    stepType = "Fork",
                    action = $"Fork gateway activated. Concurrently spawned {forkBranches.Count} branches: [{string.Join(", ", forkBranches)}].",
                    state = currentState
                });

                EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);

                if (forkBranches.Any())
                {
                    currentStepId = forkBranches.First();
                    var targetStepObj = blueprint.Workflow?.Steps.FirstOrDefault(s =>
                        string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
                    if (targetStepObj != null)
                    {
                        EvaluateAndRecordActions(targetStepObj.OnEntry, "OnEntry", targetStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                    }
                    continue;
                }
                break;
            }

            // --- JOIN STEP ---
            if (stepTypeLower.Contains("join"))
            {
                var policy = !string.IsNullOrWhiteSpace(step.JoinPolicy) ? step.JoinPolicy : "WaitAll";
                var inbounds = step.InboundSteps ?? new List<string>();

                totalStepsExecuted++;
                executionTrace.Add(new
                {
                    stepNumber = totalStepsExecuted,
                    stepId = step.StepId,
                    stepType = "Join",
                    action = $"Join gateway reached. Synchronized parallel inbound branches [{string.Join(", ", inbounds)}] with policy '{policy}'.",
                    state = currentState
                });

                EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);

                string? nextTarget = null;
                if (step.NextSteps != null && step.NextSteps.TryGetValue("Default", out var dt)) nextTarget = dt;
                else if (step.NextSteps != null && step.NextSteps.Any()) nextTarget = step.NextSteps.First().Value;

                if (!string.IsNullOrEmpty(nextTarget))
                {
                    currentStepId = nextTarget;
                    if (!string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
                    {
                        var targetStepObj = blueprint.Workflow?.Steps.FirstOrDefault(s =>
                            string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
                        if (targetStepObj != null)
                        {
                            EvaluateAndRecordActions(targetStepObj.OnEntry, "OnEntry", targetStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                        }
                    }
                    continue;
                }
                break;
            }

            // --- HUMAN TASK STEP ---
            if (stepTypeLower.Contains("human"))
            {
                var roles = GetStepRoles(step);
                bool authorized = IsRoleAuthorized(roles, simulatedRole);
                var remindersPreview = (step.Sla?.Reminders != null && step.Sla.Reminders.Count > 0)
                    ? step.Sla.Reminders.Select(r => new
                    {
                        duration = r.Duration,
                        triggerEvent = r.TriggerEvent,
                        offsetType = r.Duration.Trim().StartsWith("-") ? "BeforeDeadline" : "AfterEntry"
                    }).ToList()
                    : null;

                if (!authorized)
                {
                    status = "WaitingForHumanTask";
                    pendingHumanTask = new
                    {
                        stepId = step.StepId,
                        requiredRoles = roles,
                        allowedEvents = step.NextSteps?.Keys.ToList() ?? new List<string>(),
                        sla = step.Sla,
                        reminders = remindersPreview,
                        unauthorizedAttempt = true,
                        simulatedRole
                    };
                    executionTrace.Add(new
                    {
                        stepNumber = totalStepsExecuted + 1,
                        stepId = step.StepId,
                        stepType = "HumanTask",
                        action = $"Role mismatch: Step requires [{string.Join(", ", roles)}], but simulated role is '{simulatedRole}'. Task cannot be completed by this role.",
                        state = currentState
                    });
                    break;
                }

                if (eventsQueue.Count > 0)
                {
                    var evt = eventsQueue.Dequeue();
                    var nextSteps = step.NextSteps ?? new Dictionary<string, string>();

                    var nextStepPair = nextSteps.FirstOrDefault(kvp =>
                        string.Equals(kvp.Key, evt, StringComparison.OrdinalIgnoreCase));

                    if (string.IsNullOrEmpty(nextStepPair.Value))
                    {
                        status = "InvalidEvent";
                        executionTrace.Add(new
                        {
                            stepNumber = totalStepsExecuted + 1,
                            stepId = step.StepId,
                            stepType = "HumanTask",
                            action = $"Event '{evt}' is not valid for HumanTask step '{step.StepId}'. Expected one of: [{string.Join(", ", nextSteps.Keys)}].",
                            state = currentState
                        });
                        break;
                    }

                    var targetStep = nextStepPair.Value;

                    var (smTrans, guardBlocked, guardReason) = FindTransition(blueprint.StateMachine, currentState, evt, payload);
                    if (guardBlocked)
                    {
                        status = "BlockedByGuard";
                        executionTrace.Add(new
                        {
                            stepNumber = totalStepsExecuted + 1,
                            stepId = step.StepId,
                            stepType = "HumanTask",
                            action = $"Transition guard failed: {guardReason}",
                            state = currentState
                        });
                        break;
                    }

                    if (smTrans != null && !string.IsNullOrWhiteSpace(smTrans.ToState))
                    {
                        stateTransitions.Add(new
                        {
                            from = currentState,
                            to = smTrans.ToState,
                            eventId = evt
                        });
                        currentState = smTrans.ToState;
                    }

                    bool isReminderEvent = step.Sla?.Reminders?.Any(r => string.Equals(r.TriggerEvent, evt, StringComparison.OrdinalIgnoreCase)) ?? false;
                    string actionDesc = isReminderEvent
                        ? $"[SLA Reminder Fired] Reminder event '{evt}' dispatched. Advanced to '{targetStep}'. State is now '{currentState}'."
                        : $"Fired event '{evt}' with role '{simulatedRole}'. Advanced to '{targetStep}'. State is now '{currentState}'.";

                    totalStepsExecuted++;
                    executionTrace.Add(new
                    {
                        stepNumber = totalStepsExecuted,
                        stepId = step.StepId,
                        stepType = "HumanTask",
                        action = actionDesc,
                        state = currentState
                    });

                    EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                    currentStepId = targetStep;
                    if (!string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
                    {
                        var targetStepObj = blueprint.Workflow?.Steps.FirstOrDefault(s =>
                            string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
                        if (targetStepObj != null)
                        {
                            EvaluateAndRecordActions(targetStepObj.OnEntry, "OnEntry", targetStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                        }
                    }
                    continue;
                }
                else
                {
                    status = "WaitingForHumanTask";
                    pendingHumanTask = new
                    {
                        stepId = step.StepId,
                        requiredRoles = roles,
                        allowedEvents = step.NextSteps?.Keys.ToList() ?? new List<string>(),
                        sla = step.Sla,
                        reminders = remindersPreview
                    };
                    executionTrace.Add(new
                    {
                        stepNumber = totalStepsExecuted + 1,
                        stepId = step.StepId,
                        stepType = "HumanTask",
                        action = $"Workflow paused at HumanTask '{step.StepId}'. Waiting for event dispatch from authorized role [{string.Join(", ", roles)}].",
                        state = currentState
                    });
                    break;
                }
            }

            // --- SUBWORKFLOW STEP ---
            if (stepTypeLower.Contains("subworkflow"))
            {
                var nextSteps = step.NextSteps ?? new Dictionary<string, string>();
                var subRef = step.SubWorkflow;

                // 1. Attempt to resolve child workflow blueprint
                WorkflowClassBlueprint? childBlueprint = null;
                string childWorkflowName = subRef?.WorkflowName ?? step.StepId;

                // A. Check inline dictionary
                if (inlineSubWorkflows != null)
                {
                    if (!string.IsNullOrWhiteSpace(subRef?.WorkflowName) && inlineSubWorkflows.TryGetValue(subRef.WorkflowName, out var cb1))
                    {
                        childBlueprint = cb1;
                        childWorkflowName = subRef.WorkflowName;
                    }
                    else if (subRef?.WorkflowClassId.HasValue == true && inlineSubWorkflows.TryGetValue(subRef.WorkflowClassId.Value.ToString(), out var cb2))
                    {
                        childBlueprint = cb2;
                    }
                    else if (inlineSubWorkflows.TryGetValue(step.StepId, out var cb3))
                    {
                        childBlueprint = cb3;
                    }
                    else if (inlineSubWorkflows.Count == 1)
                    {
                        childBlueprint = inlineSubWorkflows.Values.First();
                        childWorkflowName = inlineSubWorkflows.Keys.First();
                    }
                }

                // B. Check mediator if not found inline
                if (childBlueprint == null && _mediator != null && tenantId != Guid.Empty)
                {
                    try
                    {
                        if (subRef?.WorkflowClassId.HasValue == true && subRef.WorkflowClassId.Value != Guid.Empty)
                        {
                            var wc = await _mediator.Send(new GetWorkflowClassByIdQuery(tenantId, subRef.WorkflowClassId.Value));
                            if (wc?.Definition != null)
                            {
                                childBlueprint = wc.Definition;
                                childWorkflowName = $"{wc.Name} v{wc.Version}";
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(subRef?.WorkflowName))
                        {
                            var list = await _mediator.Send(new ListWorkflowClassesQuery(tenantId, null, null));
                            var match = list.FirstOrDefault(w => string.Equals(w.Name, subRef.WorkflowName, StringComparison.OrdinalIgnoreCase));
                            if (match?.Definition != null)
                            {
                                childBlueprint = match.Definition;
                                childWorkflowName = $"{match.Name} v{match.Version}";
                            }
                        }
                    }
                    catch
                    {
                        // Fallback safely if mediator query fails
                    }
                }

                // 2. If child blueprint was resolved and recursion limit not reached:
                if (childBlueprint != null && recursionDepth < 5)
                {
                    var childInputPayload = BuildSubWorkflowInputPayload(step, payload);
                    childInputPayload["ParentStepId"] = step.StepId;

                    var childResult = await ExecuteSimulationRunAsync(
                        childBlueprint,
                        childWorkflowName,
                        childInputPayload,
                        simulatedRole,
                        childEventsQueue != null && childEventsQueue.Count > 0 ? childEventsQueue : new Queue<string>(),
                        maxSteps,
                        simulateFailureAtStep: null,
                        tenantId,
                        inlineSubWorkflows,
                        autoCompleteSubWorkflows,
                        recursionDepth + 1,
                        childEventsQueue: null,
                        autoAdvanceTimers: autoAdvanceTimers
                    );

                    subworkflowsExecuted.Add(new
                    {
                        parentStepId = step.StepId,
                        childWorkflow = childWorkflowName,
                        childStatus = childResult.Status,
                        childInitialState = childResult.InitialState,
                        childFinalState = childResult.FinalState,
                        childStepsExecuted = childResult.TotalStepsExecuted,
                        childExecutionTrace = childResult.ExecutionTrace,
                        childStateTransitions = childResult.StateTransitions,
                        childPayload = childResult.Payload
                    });

                    if (string.Equals(childResult.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplySubWorkflowOutputMapping(step, payload, childResult.Payload, childResult.FinalState, childResult.Status);
                        var completionEvent = ResolveSubWorkflowCompletionEventType(step, childResult.FinalState) ?? "SubWorkflowCompleted";

                        string? targetStep = null;
                        if (nextSteps.TryGetValue(completionEvent, out var ts)) targetStep = ts;
                        else if (nextSteps.TryGetValue("Default", out var ds)) targetStep = ds;
                        else if (nextSteps.Count > 0) targetStep = nextSteps.First().Value;

                        if (!string.IsNullOrEmpty(targetStep))
                        {
                            var (smTrans, guardBlocked, guardReason) = FindTransition(blueprint.StateMachine, currentState, completionEvent, payload);
                            if (guardBlocked)
                            {
                                status = "BlockedByGuard";
                                executionTrace.Add(new
                                {
                                    stepNumber = totalStepsExecuted + 1,
                                    stepId = step.StepId,
                                    stepType = "SubWorkflow",
                                    action = $"Child workflow '{childWorkflowName}' completed, but parent transition guard failed: {guardReason}",
                                    state = currentState
                                });
                                break;
                            }

                            if (smTrans != null && !string.IsNullOrWhiteSpace(smTrans.ToState))
                            {
                                stateTransitions.Add(new { from = currentState, to = smTrans.ToState, eventId = completionEvent });
                                currentState = smTrans.ToState;
                            }

                            totalStepsExecuted++;
                            executionTrace.Add(new
                            {
                                stepNumber = totalStepsExecuted,
                                stepId = step.StepId,
                                stepType = "SubWorkflow",
                                action = $"SubWorkflow step '{step.StepId}' executed child '{childWorkflowName}' to completion (Final state: '{childResult.FinalState}'). Output mapping applied. Fired '{completionEvent}' => Advanced to '{targetStep}'.",
                                state = currentState
                            });

                            EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                            currentStepId = targetStep;
                            if (!string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
                            {
                                var targetStepObj = blueprint.Workflow?.Steps.FirstOrDefault(s =>
                                    string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
                                if (targetStepObj != null)
                                {
                                    EvaluateAndRecordActions(targetStepObj.OnEntry, "OnEntry", targetStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                                }
                            }
                            continue;
                        }
                        else
                        {
                            status = "Completed";
                            executionTrace.Add(new
                            {
                                stepNumber = totalStepsExecuted + 1,
                                stepId = step.StepId,
                                stepType = "SubWorkflow",
                                action = $"SubWorkflow step '{step.StepId}' child completed with no outgoing transitions. Workflow finished.",
                                state = currentState
                            });
                            break;
                        }
                    }
                    else
                    {
                        status = "WaitingForSubWorkflow";
                        pendingSubWorkflow = new
                        {
                            stepId = step.StepId,
                            childWorkflow = childWorkflowName,
                            childStatus = childResult.Status,
                            childCurrentStepId = childResult.CurrentStepId,
                            childPendingHumanTask = childResult.PendingHumanTask,
                            childPendingSubWorkflow = childResult.PendingSubWorkflow,
                            allowedEvents = nextSteps.Keys.ToList()
                        };
                        executionTrace.Add(new
                        {
                            stepNumber = totalStepsExecuted + 1,
                            stepId = step.StepId,
                            stepType = "SubWorkflow",
                            action = $"Parent workflow paused at SubWorkflow step '{step.StepId}'. Child workflow '{childWorkflowName}' status is '{childResult.Status}'.",
                            state = currentState
                        });
                        break;
                    }
                }

                // 3. Child blueprint not resolved - check events queue
                if (eventsQueue.Count > 0)
                {
                    var evt = eventsQueue.Dequeue();
                    var match = nextSteps.FirstOrDefault(kvp => string.Equals(kvp.Key, evt, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(match.Value))
                    {
                        var targetStep = match.Value;
                        var (smTrans, guardBlocked, guardReason) = FindTransition(blueprint.StateMachine, currentState, evt, payload);
                        if (guardBlocked)
                        {
                            status = "BlockedByGuard";
                            executionTrace.Add(new
                            {
                                stepNumber = totalStepsExecuted + 1,
                                stepId = step.StepId,
                                stepType = "SubWorkflow",
                                action = $"Transition guard failed: {guardReason}",
                                state = currentState
                            });
                            break;
                        }

                        if (smTrans != null && !string.IsNullOrWhiteSpace(smTrans.ToState))
                        {
                            stateTransitions.Add(new { from = currentState, to = smTrans.ToState, eventId = evt });
                            currentState = smTrans.ToState;
                        }

                        payload["SubWorkflowCompleted"] = true;
                        totalStepsExecuted++;
                        executionTrace.Add(new
                        {
                            stepNumber = totalStepsExecuted,
                            stepId = step.StepId,
                            stepType = "SubWorkflow",
                            action = $"SubWorkflow completion event '{evt}' received from events queue. Advanced to '{targetStep}'.",
                            state = currentState
                        });

                        EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                        currentStepId = targetStep;
                        if (!string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
                        {
                            var targetStepObj = blueprint.Workflow?.Steps.FirstOrDefault(s =>
                                string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
                            if (targetStepObj != null)
                            {
                                EvaluateAndRecordActions(targetStepObj.OnEntry, "OnEntry", targetStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                            }
                        }
                        continue;
                    }
                }

                // 4. If autoCompleteSubWorkflows is true, auto-complete
                if (autoCompleteSubWorkflows)
                {
                    var completionEvent = ResolveSubWorkflowCompletionEventType(step) ?? "SubWorkflowCompleted";
                    string? targetStep = null;
                    if (nextSteps.TryGetValue(completionEvent, out var ts)) targetStep = ts;
                    else if (nextSteps.TryGetValue("Default", out var ds)) targetStep = ds;
                    else if (nextSteps.Count > 0) targetStep = nextSteps.First().Value;

                    if (!string.IsNullOrEmpty(targetStep))
                    {
                        var (smTrans, guardBlocked, guardReason) = FindTransition(blueprint.StateMachine, currentState, completionEvent, payload);
                        if (guardBlocked)
                        {
                            status = "BlockedByGuard";
                            executionTrace.Add(new
                            {
                                stepNumber = totalStepsExecuted + 1,
                                stepId = step.StepId,
                                stepType = "SubWorkflow",
                                action = $"Transition guard failed: {guardReason}",
                                state = currentState
                            });
                            break;
                        }

                        if (smTrans != null && !string.IsNullOrWhiteSpace(smTrans.ToState))
                        {
                            stateTransitions.Add(new { from = currentState, to = smTrans.ToState, eventId = completionEvent });
                            currentState = smTrans.ToState;
                        }

                        payload["SubWorkflowCompleted"] = true;
                        totalStepsExecuted++;
                        executionTrace.Add(new
                        {
                            stepNumber = totalStepsExecuted,
                            stepId = step.StepId,
                            stepType = "SubWorkflow",
                            action = $"Auto-completed SubWorkflow step '{step.StepId}' (event: '{completionEvent}'). Advanced to '{targetStep}'.",
                            state = currentState
                        });

                        EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                        currentStepId = targetStep;
                        if (!string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
                        {
                            var targetStepObj = blueprint.Workflow?.Steps.FirstOrDefault(s =>
                                string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
                            if (targetStepObj != null)
                            {
                                EvaluateAndRecordActions(targetStepObj.OnEntry, "OnEntry", targetStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                            }
                        }
                        continue;
                    }
                }

                // 5. Otherwise, pause waiting for completion
                status = "WaitingForSubWorkflow";
                pendingSubWorkflow = new
                {
                    stepId = step.StepId,
                    subWorkflow = step.SubWorkflow != null ? new
                    {
                        workflowName = step.SubWorkflow.WorkflowName,
                        workflowClassId = step.SubWorkflow.WorkflowClassId,
                        inputMapping = step.SubWorkflow.InputMapping,
                        outputMapping = step.SubWorkflow.OutputMapping
                    } : null,
                    allowedEvents = nextSteps.Keys.ToList(),
                    reason = "Child workflow blueprint not provided or not resolved; waiting for child completion event."
                };
                executionTrace.Add(new
                {
                    stepNumber = totalStepsExecuted + 1,
                    stepId = step.StepId,
                    stepType = "SubWorkflow",
                    action = $"Workflow paused at SubWorkflow step '{step.StepId}'. Waiting for child completion event [{string.Join(", ", nextSteps.Keys)}].",
                    state = currentState
                });
                break;
            }

            // --- TIMER STEP ---
            if (stepTypeLower.Contains("timer"))
            {
                var timerDetails = ResolveTimerDetails(step, payload);
                var nextSteps = step.NextSteps ?? new Dictionary<string, string>();

                if (eventsQueue.Count > 0)
                {
                    var peekEvt = eventsQueue.Peek();
                    var match = nextSteps.FirstOrDefault(kvp => string.Equals(kvp.Key, peekEvt, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(match.Value))
                    {
                        var evt = eventsQueue.Dequeue();
                        var targetStep = match.Value;
                        var (smTrans, guardBlocked, guardReason) = FindTransition(blueprint.StateMachine, currentState, evt, payload);
                        if (guardBlocked)
                        {
                            status = "BlockedByGuard";
                            executionTrace.Add(new
                            {
                                stepNumber = totalStepsExecuted + 1,
                                stepId = step.StepId,
                                stepType = "Timer",
                                action = $"Transition guard failed for timer event '{evt}': {guardReason}",
                                state = currentState
                            });
                            break;
                        }
                        if (smTrans != null && !string.IsNullOrWhiteSpace(smTrans.ToState))
                        {
                            stateTransitions.Add(new { from = currentState, to = smTrans.ToState, eventId = evt });
                            currentState = smTrans.ToState;
                        }
                        totalStepsExecuted++;
                        executionTrace.Add(new
                        {
                            stepNumber = totalStepsExecuted,
                            stepId = step.StepId,
                            stepType = "Timer",
                            action = $"Timer step elapsed via event '{evt}'. Advanced to '{targetStep}'. [{timerDetails.Description}]",
                            state = currentState
                        });

                        EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                        currentStepId = targetStep;
                        if (!string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
                        {
                            var targetStepObj = blueprint.Workflow?.Steps.FirstOrDefault(s =>
                                string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
                            if (targetStepObj != null)
                            {
                                EvaluateAndRecordActions(targetStepObj.OnEntry, "OnEntry", targetStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                            }
                        }
                        continue;
                    }
                }

                if (autoAdvanceTimers)
                {
                    string? targetStep = null;
                    string? triggeredEvent = null;

                    if (nextSteps.TryGetValue("Default", out var dt))
                    {
                        targetStep = dt;
                        triggeredEvent = "Default";
                    }
                    else if (nextSteps.Count > 0)
                    {
                        var first = nextSteps.First();
                        triggeredEvent = first.Key;
                        targetStep = first.Value;
                    }

                    if (!string.IsNullOrEmpty(targetStep))
                    {
                        if (!string.IsNullOrEmpty(triggeredEvent) && !string.Equals(triggeredEvent, "Default", StringComparison.OrdinalIgnoreCase))
                        {
                            var (smTrans, guardBlocked, guardReason) = FindTransition(blueprint.StateMachine, currentState, triggeredEvent, payload);
                            if (guardBlocked)
                            {
                                status = "BlockedByGuard";
                                executionTrace.Add(new
                                {
                                    stepNumber = totalStepsExecuted + 1,
                                    stepId = step.StepId,
                                    stepType = "Timer",
                                    action = $"Transition guard failed for auto-advanced timer event '{triggeredEvent}': {guardReason}",
                                    state = currentState
                                });
                                break;
                            }

                            if (smTrans != null && !string.IsNullOrWhiteSpace(smTrans.ToState))
                            {
                                stateTransitions.Add(new { from = currentState, to = smTrans.ToState, eventId = triggeredEvent });
                                currentState = smTrans.ToState;
                            }
                        }

                        totalStepsExecuted++;
                        executionTrace.Add(new
                        {
                            stepNumber = totalStepsExecuted,
                            stepId = step.StepId,
                            stepType = "Timer",
                            action = $"Timer auto-advanced (autoAdvanceTimers=true) via '{triggeredEvent}'. Advanced to '{targetStep}'. [{timerDetails.Description}]",
                            state = currentState
                        });

                        EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                        currentStepId = targetStep;
                        if (!string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
                        {
                            var targetStepObj = blueprint.Workflow?.Steps.FirstOrDefault(s =>
                                string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
                            if (targetStepObj != null)
                            {
                                EvaluateAndRecordActions(targetStepObj.OnEntry, "OnEntry", targetStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                            }
                        }
                        continue;
                    }
                }

                status = "WaitingForTimer";
                pendingTimer = new
                {
                    stepId = step.StepId,
                    timerType = timerDetails.TimerType,
                    targetProperty = timerDetails.TargetProperty,
                    rawTargetValue = timerDetails.RawTargetValue,
                    baseTimestampUtc = timerDetails.BaseTimestampUtc?.ToString("o"),
                    offset = timerDetails.OffsetStr,
                    dueTimeUtc = timerDetails.ScheduledDueUtc?.ToString("o"),
                    duration = timerDetails.DurationSpan.HasValue ? timerDetails.DurationSpan.Value.ToString() : null,
                    allowedEvents = nextSteps.Keys.ToList(),
                    description = timerDetails.Description
                };

                executionTrace.Add(new
                {
                    stepNumber = totalStepsExecuted + 1,
                    stepId = step.StepId,
                    stepType = "Timer",
                    action = $"Workflow paused at Timer step '{step.StepId}'. {timerDetails.Description}.",
                    state = currentState
                });
                break;
            }

            // --- AUTOMATED STEP (Command, Event, etc.) ---
            {
                var nextSteps = step.NextSteps ?? new Dictionary<string, string>();
                string? targetStep = null;
                string? triggeredEvent = null;

                if (eventsQueue.Count > 0)
                {
                    var peekEvt = eventsQueue.Peek();
                    var match = nextSteps.FirstOrDefault(kvp => string.Equals(kvp.Key, peekEvt, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(match.Value))
                    {
                        triggeredEvent = eventsQueue.Dequeue();
                        targetStep = match.Value;
                    }
                }

                if (targetStep == null)
                {
                    if (nextSteps.TryGetValue("Default", out var def))
                    {
                        targetStep = def;
                        triggeredEvent = "Default";
                    }
                    else if (nextSteps.Count == 1)
                    {
                        var single = nextSteps.First();
                        triggeredEvent = single.Key;
                        targetStep = single.Value;
                    }
                    else if (nextSteps.Count > 1)
                    {
                        var first = nextSteps.First();
                        triggeredEvent = first.Key;
                        targetStep = first.Value;
                    }
                }

                if (targetStep == null)
                {
                    status = "Completed";
                    executionTrace.Add(new
                    {
                        stepNumber = totalStepsExecuted + 1,
                        stepId = step.StepId,
                        stepType = stepType,
                        action = $"Automated step '{step.StepId}' completed with no outgoing transitions. Workflow finished.",
                        state = currentState
                    });
                    break;
                }

                if (!string.IsNullOrEmpty(triggeredEvent) && !string.Equals(triggeredEvent, "Default", StringComparison.OrdinalIgnoreCase))
                {
                    var (smTrans, guardBlocked, guardReason) = FindTransition(blueprint.StateMachine, currentState, triggeredEvent, payload);
                    if (guardBlocked)
                    {
                        status = "BlockedByGuard";
                        executionTrace.Add(new
                        {
                            stepNumber = totalStepsExecuted + 1,
                            stepId = step.StepId,
                            stepType = stepType,
                            action = $"Transition guard failed for automated event '{triggeredEvent}': {guardReason}",
                            state = currentState
                        });
                        break;
                    }

                    if (smTrans != null && !string.IsNullOrWhiteSpace(smTrans.ToState))
                    {
                        stateTransitions.Add(new
                        {
                            from = currentState,
                            to = smTrans.ToState,
                            eventId = triggeredEvent
                        });
                        currentState = smTrans.ToState;
                    }
                }

                totalStepsExecuted++;
                executionTrace.Add(new
                {
                    stepNumber = totalStepsExecuted,
                    stepId = step.StepId,
                    stepType = stepType,
                    action = $"Automated step '{step.StepId}' executed" + (!string.IsNullOrEmpty(triggeredEvent) && triggeredEvent != "Default" ? $" (event: '{triggeredEvent}')" : "") + $". Advanced to '{targetStep}'. State is '{currentState}'.",
                    state = currentState
                });

                EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                currentStepId = targetStep;
                if (!string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
                {
                    var targetStepObj = blueprint.Workflow?.Steps.FirstOrDefault(s =>
                        string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
                    if (targetStepObj != null)
                    {
                        EvaluateAndRecordActions(targetStepObj.OnEntry, "OnEntry", targetStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                    }
                }
            }
        }

        if (totalStepsExecuted >= maxSteps && status == "Running")
        {
            status = "MaxStepsExceeded";
            executionTrace.Add(new
            {
                stepNumber = totalStepsExecuted,
                stepId = currentStepId,
                stepType = "Limit",
                action = $"Simulation reached maximum step limit of {maxSteps}. Possible loop detected.",
                state = currentState
            });
        }

        return new SimulationRunResult(
            sourceWorkflowName,
            status,
            initialState,
            currentState,
            startStepId,
            currentStepId,
            totalStepsExecuted,
            simulatedRole,
            pendingHumanTask,
            pendingSubWorkflow,
            decisionsEvaluated,
            stateTransitions,
            actionsTriggered,
            executionTrace,
            payload,
            subworkflowsExecuted,
            pendingTimer
        );
    }


    public async Task<CallToolResult> SimulateCompensationPath(JObject args)
    {
        try
        {
            if (_compensationPlanner == null)
            {
                return McpToolResults.Fail("MCP-INTERNAL", "Compensation planner service is not registered.");
            }

            WorkflowClassBlueprint? blueprint = null;
            var inlineBlueprintToken = args["blueprint"] as JObject;
            if (inlineBlueprintToken != null)
            {
                blueprint = inlineBlueprintToken.ToObject<WorkflowClassBlueprint>();
            }
            else
            {
                var idStr = args["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(idStr) || !Guid.TryParse(idStr, out var id))
                {
                    return McpToolResults.Fail("MCP-ARG-001", "Either 'blueprint' or UUID 'id' is required.");
                }

                Guid tenantId;
                try
                {
                    tenantId = McpTenantResolver.ResolveRequired(args);
                }
                catch (McpToolException ex)
                {
                    return McpToolResults.Fail(ex.Code, ex.Message);
                }

                var workflowClass = await _mediator.Send(new GetWorkflowClassByIdQuery(tenantId, id));
                if (workflowClass?.Definition == null)
                {
                    return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found or has no definition.");
                }
                blueprint = workflowClass.Definition;
            }

            if (blueprint?.Workflow?.Steps == null || blueprint.Workflow.Steps.Count == 0)
            {
                return McpToolResults.Fail("MCP-ARG-001", "Workflow blueprint contains no steps.");
            }

            var failedStepId = args["failedStepId"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(failedStepId))
            {
                return McpToolResults.Fail("MCP-ARG-001", "failedStepId is required.");
            }

            var executedSteps = new List<string>();
            if (args["executedStepIds"] is JArray arr && arr.Count > 0)
            {
                executedSteps.AddRange(arr.Select(x => x?.ToString() ?? string.Empty));
            }
            else
            {
                // Fallback path estimation: steps declared up to and including failedStepId.
                foreach (var step in blueprint.Workflow.Steps)
                {
                    executedSteps.Add(step.StepId);
                    if (string.Equals(step.StepId, failedStepId, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }

            var actionMap = new Dictionary<string, List<FlowOS.Core.Common.Interfaces.CompensationActionDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in blueprint.Workflow.Steps)
            {
                var actions = (step.OnFailure ?? new List<StepActionBlueprint>())
                    .Select(a => new FlowOS.Core.Common.Interfaces.CompensationActionDto(
                        StepId: step.StepId,
                        Hook: "OnFailure",
                        ActionType: a.ActionType,
                        Target: a.Target,
                        Url: a.Url,
                        Condition: a.Condition,
                        Capability: a.Capability ?? a.Target))
                    .ToList();
                actionMap[step.StepId] = actions;
            }

            var plan = _compensationPlanner.Plan(new FlowOS.Core.Common.Interfaces.CompensationPathRequestDto(
                FailedStepId: failedStepId,
                ExecutedStepIds: executedSteps,
                OnFailureActionsByStep: actionMap));

            return McpToolResults.Success(new
            {
                failedStepId = plan.FailedStepId,
                executedStepIds = executedSteps,
                isFullyCompensable = plan.IsFullyCompensable,
                orderedCompensations = plan.OrderedCompensations.Select(p => new
                {
                    stepId = p.StepId,
                    actionCount = p.ActionCount,
                    actions = p.Actions.Select(a => new
                    {
                        stepId = a.StepId,
                        hook = a.Hook,
                        actionType = a.ActionType,
                        target = a.Target,
                        capability = a.Capability,
                        url = a.Url,
                        condition = a.Condition
                    })
                }),
                blockedSteps = plan.BlockedSteps
            });
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to simulate compensation path: {ex.Message}");
        }
    }

    public async Task<CallToolResult> SimulateParallelExecution(JObject args)
    {
        try
        {
            WorkflowClassBlueprint? blueprint = null;
            string sourceWorkflowName = "InlineBlueprint";

            var inlineBlueprintToken = args["blueprint"] as JObject;
            if (inlineBlueprintToken != null)
            {
                blueprint = inlineBlueprintToken.ToObject<WorkflowClassBlueprint>();
            }
            else
            {
                var idStr = args["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(idStr) || !Guid.TryParse(idStr, out var id))
                {
                    return McpToolResults.Fail("MCP-ARG-001", "Either 'blueprint' or UUID 'id' must be provided.");
                }

                Guid tenantId;
                try
                {
                    tenantId = McpTenantResolver.ResolveRequired(args);
                }
                catch (McpToolException ex)
                {
                    return McpToolResults.Fail(ex.Code, ex.Message);
                }

                var workflowClass = await _mediator.Send(new GetWorkflowClassByIdQuery(tenantId, id));
                if (workflowClass?.Definition == null)
                {
                    return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found or has no definition.");
                }

                blueprint = workflowClass.Definition;
                sourceWorkflowName = workflowClass.Name;
            }

            if (blueprint?.Workflow?.Steps == null || blueprint.Workflow.Steps.Count == 0)
            {
                return McpToolResults.Fail("MCP-ARG-001", "Workflow blueprint contains no steps.");
            }

            var payload = ToPayloadDictionary(args["payload"] as JObject);
            var forkStep = blueprint.Workflow.Steps.FirstOrDefault(s => string.Equals(s.StepType, "Fork", StringComparison.OrdinalIgnoreCase));
            if (forkStep == null)
            {
                return McpToolResults.Fail("MCP-ARG-001", "Workflow blueprint does not contain a 'Fork' step.");
            }

            var forkBranches = (forkStep.Branches != null && forkStep.Branches.Count > 0)
                ? forkStep.Branches
                : (forkStep.NextSteps != null ? forkStep.NextSteps.Values.Distinct().ToList() : new List<string>());

            var joinStep = blueprint.Workflow.Steps.FirstOrDefault(s => string.Equals(s.StepType, "Join", StringComparison.OrdinalIgnoreCase));
            var joinPolicy = joinStep?.JoinPolicy ?? "WaitAll";

            var executionTrace = new List<object>();
            var actionsTriggered = new List<object>();
            var branchResults = new List<object>();
            int stepCounter = 1;

            executionTrace.Add(new
            {
                stepNumber = stepCounter++,
                stepId = forkStep.StepId,
                stepType = "Fork",
                action = $"Fork gateway activated. Concurrently spawning {forkBranches.Count} branches: [{string.Join(", ", forkBranches)}].",
                state = blueprint.StateMachine?.InitialState ?? "Running"
            });

            EvaluateAndRecordActions(forkStep.OnExit, "OnExit", forkStep.StepId, payload, actionsTriggered, executionTrace, stepCounter);

            // Simulate each parallel branch
            foreach (var branchStartId in forkBranches)
            {
                var branchTrace = new List<string>();
                var branchActions = new List<object>();
                var currentBranchStepId = branchStartId;
                int branchStepCount = 0;
                bool reachedJoinOrEnd = false;

                while (!string.IsNullOrEmpty(currentBranchStepId) &&
                       !string.Equals(currentBranchStepId, "END", StringComparison.OrdinalIgnoreCase) &&
                       branchStepCount < 20)
                {
                    var stepObj = blueprint.Workflow.Steps.FirstOrDefault(s =>
                        string.Equals(s.StepId, currentBranchStepId, StringComparison.OrdinalIgnoreCase));

                    if (stepObj == null) break;

                    if (joinStep != null && string.Equals(stepObj.StepId, joinStep.StepId, StringComparison.OrdinalIgnoreCase))
                    {
                        reachedJoinOrEnd = true;
                        branchTrace.Add($"Branch '{branchStartId}' reached Join gateway '{joinStep.StepId}'.");
                        break;
                    }

                    branchStepCount++;
                    branchTrace.Add($"Executed branch step '{stepObj.StepId}' ({stepObj.StepType}).");

                    EvaluateAndRecordActions(stepObj.OnEntry, "OnEntry", stepObj.StepId, payload, branchActions, executionTrace, stepCounter++);
                    EvaluateAndRecordActions(stepObj.OnExit, "OnExit", stepObj.StepId, payload, branchActions, executionTrace, stepCounter++);

                    string? nextTarget = null;
                    if (string.Equals(stepObj.StepType, "SubWorkflow", StringComparison.OrdinalIgnoreCase))
                    {
                        var compKey = ResolveSubWorkflowCompletionEventType(stepObj);
                        if (!string.IsNullOrEmpty(compKey) && stepObj.NextSteps != null && stepObj.NextSteps.TryGetValue(compKey, out var cTarget))
                        {
                            nextTarget = cTarget;
                        }
                    }

                    if (nextTarget == null)
                    {
                        if (stepObj.NextSteps != null && stepObj.NextSteps.TryGetValue("Default", out var def)) nextTarget = def;
                        else if (stepObj.NextSteps != null && stepObj.NextSteps.Count > 0) nextTarget = stepObj.NextSteps.First().Value;
                    }

                    currentBranchStepId = nextTarget;
                }

                actionsTriggered.AddRange(branchActions);
                branchResults.Add(new
                {
                    branchId = branchStartId,
                    stepCount = branchStepCount,
                    reachedJoin = reachedJoinOrEnd,
                    actionsCount = branchActions.Count,
                    trace = branchTrace
                });
            }

            string? resumedStepId = null;
            if (joinStep != null)
            {
                executionTrace.Add(new
                {
                    stepNumber = stepCounter++,
                    stepId = joinStep.StepId,
                    stepType = "Join",
                    action = $"Join barrier reached. Synchronized all {forkBranches.Count} parallel branches under policy '{joinPolicy}'.",
                    state = blueprint.StateMachine?.States?.LastOrDefault() ?? "Synchronized"
                });

                EvaluateAndRecordActions(joinStep.OnExit, "OnExit", joinStep.StepId, payload, actionsTriggered, executionTrace, stepCounter);

                if (joinStep.NextSteps != null && joinStep.NextSteps.TryGetValue("Default", out var jDef)) resumedStepId = jDef;
                else if (joinStep.NextSteps != null && joinStep.NextSteps.Count > 0) resumedStepId = joinStep.NextSteps.First().Value;
            }

            return McpToolResults.Success(new
            {
                workflow = sourceWorkflowName,
                forkStepId = forkStep.StepId,
                totalBranches = forkBranches.Count,
                branches = branchResults,
                joinStepId = joinStep?.StepId,
                joinPolicy,
                isSynchronized = true,
                resumedStepId = resumedStepId ?? "END",
                actionsTriggeredCount = actionsTriggered.Count,
                actionsTriggered,
                executionTrace
            });
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to simulate parallel execution: {ex.Message}");
        }
    }

    private static void EvaluateAndRecordActions(
        List<StepActionBlueprint>? actions,
        string hookType,
        string stepId,
        Dictionary<string, object> payload,
        List<object> actionsTriggered,
        List<object> executionTrace,
        int stepNumber)
    {
        if (actions == null || actions.Count == 0) return;

        foreach (var action in actions)
        {
            bool conditionMatched = true;
            if (!string.IsNullOrWhiteSpace(action.Condition))
            {
                conditionMatched = EvaluateExpressionSafely(action.Condition, payload);
            }

            if (conditionMatched)
            {
                var resolvedTarget = ExpressionEvaluator.InterpolateTemplate(action.Target, payload);
                var resolvedUrl = ExpressionEvaluator.InterpolateTemplate(action.Url, payload);
                var interpolatedMessage = ExpressionEvaluator.InterpolateTemplate(action.Template, payload);

                Dictionary<string, object>? transformedPayload = null;
                if (action.PayloadMapping != null && action.PayloadMapping.Count > 0)
                {
                    transformedPayload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in action.PayloadMapping)
                    {
                        var val = ExpressionEvaluator.EvaluateValue(kvp.Value, payload);
                        transformedPayload[kvp.Key] = val ?? kvp.Value;
                    }

                    foreach (var kvp in transformedPayload)
                    {
                        payload[kvp.Key] = kvp.Value;
                    }
                }

                actionsTriggered.Add(new
                {
                    stepId,
                    hook = hookType,
                    actionType = action.ActionType,
                    target = resolvedTarget,
                    url = resolvedUrl,
                    condition = action.Condition,
                    template = action.Template,
                    interpolatedMessage = !string.IsNullOrEmpty(interpolatedMessage) ? interpolatedMessage : null,
                    transformedPayload,
                    status = "Executed"
                });

                var actionDesc = $"[Hook {hookType}] Executed {action.ActionType} action targeting '{resolvedTarget}'" +
                                 (!string.IsNullOrWhiteSpace(action.Condition) ? $" (Condition '{action.Condition}' matched)" : "");

                if (!string.IsNullOrEmpty(interpolatedMessage))
                {
                    actionDesc += $" | Rendered: \"{interpolatedMessage}\"";
                }
                if (transformedPayload != null && transformedPayload.Count > 0)
                {
                    var payloadSummary = string.Join(", ", transformedPayload.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                    actionDesc += $" | Transformed Payload: {{{payloadSummary}}}";
                }
                actionDesc += ".";

                executionTrace.Add(new
                {
                    stepNumber,
                    stepId,
                    stepType = "LifecycleAction",
                    action = actionDesc,
                    state = ""
                });
            }
            else
            {
                actionsTriggered.Add(new
                {
                    stepId,
                    hook = hookType,
                    actionType = action.ActionType,
                    target = action.Target,
                    condition = action.Condition,
                    status = "Skipped"
                });
                executionTrace.Add(new
                {
                    stepNumber,
                    stepId,
                    stepType = "LifecycleAction",
                    action = $"[Hook {hookType}] Skipped {action.ActionType} action targeting '{action.Target}' (Condition '{action.Condition}' evaluated to FALSE).",
                    state = ""
                });
            }
        }
    }

    private static List<string> GetStepRoles(StepBlueprint step)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (step.RequiredRoles != null)
        {
            foreach (var r in step.RequiredRoles)
            {
                if (!string.IsNullOrWhiteSpace(r)) roles.Add(r.Trim());
            }
        }
        if (step.AllowedRoles != null)
        {
            foreach (var r in step.AllowedRoles)
            {
                if (!string.IsNullOrWhiteSpace(r)) roles.Add(r.Trim());
            }
        }
        return roles.ToList();
    }

    private static bool IsRoleAuthorized(List<string> stepRoles, string simulatedRole)
    {
        if (stepRoles.Count == 0) return true;
        if (stepRoles.Any(r => string.Equals(r, "Anyone", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(r, "Unassigned", StringComparison.OrdinalIgnoreCase)))
            return true;

        return stepRoles.Any(r => string.Equals(r, simulatedRole, StringComparison.OrdinalIgnoreCase));
    }

    private static bool EvaluateExpressionSafely(string expression, Dictionary<string, object> payload)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;
        var trimmed = expression.Trim();
        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase)) return true;

        // Strip Payload. or payload.
        var sanitized = Regex.Replace(trimmed, @"\b[pP]ayload\.", "");
        // Normalize single '=' to '==' when not already part of ==, !=, <=, >=
        sanitized = Regex.Replace(sanitized, @"(?<![!<>=])=(?![=])", "==");

        try
        {
            return ExpressionEvaluator.Evaluate(sanitized, payload);
        }
        catch
        {
            return false;
        }
    }

    private static (TransitionBlueprint? transition, bool guardBlocked, string? guardReason) FindTransition(
        StateMachineBlueprint? sm,
        string currentState,
        string eventId,
        Dictionary<string, object> payload)
    {
        if (sm?.Transitions == null || sm.Transitions.Count == 0)
            return (null, false, null);

        var matching = sm.Transitions.FirstOrDefault(t =>
            (string.Equals(t.FromState, currentState, StringComparison.OrdinalIgnoreCase) || t.FromState == "*") &&
            string.Equals(t.EventId, eventId, StringComparison.OrdinalIgnoreCase));

        if (matching == null)
            return (null, false, null);

        string? constraint = matching.Condition;
        if (string.IsNullOrWhiteSpace(constraint) && matching.Constraints != null)
        {
            if (matching.Constraints.TryGetValue("Expression", out var expr))
                constraint = expr;
            else if (matching.Constraints.TryGetValue("condition", out var cond))
                constraint = cond;
            else if (matching.Constraints.Count > 0)
                constraint = matching.Constraints.Values.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(constraint))
        {
            if (!EvaluateExpressionSafely(constraint, payload))
            {
                return (matching, true, $"State machine guard condition '{constraint}' evaluated to FALSE against payload.");
            }
        }

        return (matching, false, null);
    }

    private static Dictionary<string, object> ToPayloadDictionary(JObject? jObj)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (jObj == null) return dict;

        foreach (var prop in jObj.Properties())
        {
            dict[prop.Name] = ConvertJToken(prop.Value);
        }
        return dict;
    }

    private static object ConvertJToken(JToken token)
    {
        return token.Type switch
        {
            JTokenType.Integer => (long)token,
            JTokenType.Float => (double)token,
            JTokenType.String => (string)token!,
            JTokenType.Boolean => (bool)token,
            JTokenType.Null => null!,
            JTokenType.Date => (DateTime)token,
            JTokenType.Array => ((JArray)token).Select(ConvertJToken).ToList(),
            JTokenType.Object => ToPayloadDictionary((JObject)token),
            _ => token.ToString()
        };
    }

    private static Dictionary<string, object> BuildSubWorkflowInputPayload(
        StepBlueprint parentStep,
        Dictionary<string, object>? parentPayload)
    {
        var sourcePayload = parentPayload ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var mapping = parentStep.SubWorkflow?.InputMapping;
        if (mapping == null || mapping.Count == 0)
        {
            return new Dictionary<string, object>(sourcePayload, StringComparer.OrdinalIgnoreCase);
        }

        var resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in mapping)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key)) continue;

            var evaluated = ExpressionEvaluator.EvaluateValue(kvp.Value, sourcePayload);
            resolved[kvp.Key] = evaluated ?? kvp.Value;
        }

        return resolved;
    }

    private static void ApplySubWorkflowOutputMapping(
        StepBlueprint parentStep,
        Dictionary<string, object> parentPayload,
        Dictionary<string, object> childPayload,
        string childFinalState,
        string childStatus)
    {
        parentPayload["SubWorkflowCompleted"] = true;
        parentPayload["ChildStatus"] = childStatus;
        parentPayload["ChildCurrentState"] = childFinalState ?? string.Empty;

        var mapping = parentStep.SubWorkflow?.OutputMapping;
        if (mapping != null && mapping.Count > 0)
        {
            var childContext = new Dictionary<string, object>(childPayload, StringComparer.OrdinalIgnoreCase)
            {
                ["SubWorkflowCompleted"] = true,
                ["ChildStatus"] = childStatus,
                ["ChildCurrentState"] = childFinalState ?? string.Empty,
                ["Payload"] = childPayload
            };

            foreach (var kvp in mapping)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;

                var evaluated = ExpressionEvaluator.EvaluateValue(kvp.Value, childContext);
                parentPayload[kvp.Key] = evaluated ?? kvp.Value;
            }
        }
        else
        {
            foreach (var kvp in childPayload)
            {
                if (!parentPayload.ContainsKey(kvp.Key))
                {
                    parentPayload[kvp.Key] = kvp.Value;
                }
            }
        }
    }

    private static string? ResolveSubWorkflowCompletionEventType(StepBlueprint parentStep, string? childFinalState = null)
    {
        if (parentStep.NextSteps == null || parentStep.NextSteps.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(childFinalState))
        {
            var stateMatch = parentStep.NextSteps.Keys.FirstOrDefault(k =>
                string.Equals(k, childFinalState, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(stateMatch)) return stateMatch;
        }

        var preferred = parentStep.NextSteps.Keys.FirstOrDefault(k =>
            string.Equals(k, "SubWorkflowCompleted", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred;

        var defaultKey = parentStep.NextSteps.Keys.FirstOrDefault(k =>
            string.Equals(k, "Default", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(defaultKey)) return defaultKey;

        return parentStep.NextSteps.Keys.FirstOrDefault();
    }

    private static Dictionary<string, WorkflowClassBlueprint> ParseInlineSubWorkflows(JObject args)
    {
        var inlineSubWorkflows = new Dictionary<string, WorkflowClassBlueprint>(StringComparer.OrdinalIgnoreCase);
        if (args["subWorkflows"] is JObject subWorkflowsObj)
        {
            foreach (var prop in subWorkflowsObj.Properties())
            {
                if (prop.Value is JObject childObj)
                {
                    var bp = childObj.ToObject<WorkflowClassBlueprint>();
                    if (bp != null)
                    {
                        inlineSubWorkflows[prop.Name] = bp;
                        var cName = childObj["name"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(cName))
                        {
                            inlineSubWorkflows[cName] = bp;
                        }
                    }
                }
            }
        }
        else if (args["subWorkflows"] is JArray subWorkflowsArr)
        {
            foreach (var item in subWorkflowsArr)
            {
                if (item is JObject childObj)
                {
                    var bp = childObj.ToObject<WorkflowClassBlueprint>();
                    if (bp != null)
                    {
                        var key = childObj["name"]?.ToString() ?? $"SubWorkflow_{inlineSubWorkflows.Count + 1}";
                        inlineSubWorkflows[key] = bp;
                    }
                }
            }
        }

        if (args["childBlueprint"] is JObject directChildObj)
        {
            var directBp = directChildObj.ToObject<WorkflowClassBlueprint>();
            if (directBp != null)
            {
                var key = directChildObj["name"]?.ToString() ?? "ChildWorkflow";
                inlineSubWorkflows[key] = directBp;
            }
        }

        return inlineSubWorkflows;
    }

    private static Queue<string> ParseEventsQueue(JToken? token)
    {
        var queue = new Queue<string>();
        if (token is JArray arr)
        {
            foreach (var evt in arr)
            {
                var val = evt?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(val))
                    queue.Enqueue(val);
            }
        }
        return queue;
    }

    public record ResolvedTimerInfo(
        string TimerType,
        string? TargetProperty,
        string? RawTargetValue,
        DateTime? BaseTimestampUtc,
        string? OffsetStr,
        TimeSpan? OffsetSpan,
        DateTime? ScheduledDueUtc,
        TimeSpan? DurationSpan,
        string Description
    );

    private static ResolvedTimerInfo ResolveTimerDetails(StepBlueprint step, Dictionary<string, object> payload)
    {
        var conditions = step.Conditions ?? new Dictionary<string, string>();

        // 1. Explicit scheduled timestamp condition (e.g. scheduledAt, scheduledTime, dueTime)
        string? scheduledAtStr = GetConditionValue(conditions, "scheduledAt", "scheduledTime", "dueTime");
        if (!string.IsNullOrWhiteSpace(scheduledAtStr) &&
            DateTime.TryParse(scheduledAtStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var explicitDt))
        {
            var explicitUtc = explicitDt.Kind == DateTimeKind.Utc ? explicitDt : explicitDt.ToUniversalTime();
            return new ResolvedTimerInfo(
                TimerType: "Explicit",
                TargetProperty: null,
                RawTargetValue: scheduledAtStr,
                BaseTimestampUtc: explicitUtc,
                OffsetStr: null,
                OffsetSpan: null,
                ScheduledDueUtc: explicitUtc,
                DurationSpan: null,
                Description: $"Explicit timer scheduled for {explicitUtc:O}"
            );
        }

        // 2. Relative dynamic timer (targetTimestampProperty + leadTime/offset)
        string? targetProp = GetConditionValue(conditions, "targetTimestampProperty", "targetTimestamp", "referenceDate", "targetDate", "eventDate", "property");
        if (!string.IsNullOrWhiteSpace(targetProp))
        {
            object? val = null;
            if (payload != null)
            {
                var matchKey = payload.Keys.FirstOrDefault(k => string.Equals(k, targetProp, StringComparison.OrdinalIgnoreCase));
                if (matchKey != null)
                {
                    val = payload[matchKey];
                }
            }

            DateTime baseDt = default;
            bool parsed = false;
            string? rawValStr = val?.ToString();

            if (val is DateTime dt)
            {
                baseDt = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
                parsed = true;
            }
            else if (val is string str && DateTime.TryParse(str, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var sDt))
            {
                baseDt = sDt.Kind == DateTimeKind.Utc ? sDt : sDt.ToUniversalTime();
                parsed = true;
            }
            else if (val is Newtonsoft.Json.Linq.JValue jVal && jVal.Value is DateTime jdt)
            {
                baseDt = jdt.Kind == DateTimeKind.Utc ? jdt : jdt.ToUniversalTime();
                parsed = true;
            }
            else if (val is Newtonsoft.Json.Linq.JValue jVal2 && DateTime.TryParse(jVal2.ToString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var jsDt))
            {
                baseDt = jsDt.Kind == DateTimeKind.Utc ? jsDt : jsDt.ToUniversalTime();
                parsed = true;
            }
            else if (val is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.String &&
                     DateTime.TryParse(elem.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var eDt))
            {
                baseDt = eDt.Kind == DateTimeKind.Utc ? eDt : eDt.ToUniversalTime();
                parsed = true;
            }

            string? offsetStr = GetConditionValue(conditions, "leadTime", "offset", "delay");
            var offsetSpan = !string.IsNullOrWhiteSpace(offsetStr) ? ParseDurationString(offsetStr) : TimeSpan.Zero;

            if (parsed)
            {
                var dueUtc = baseDt.Add(offsetSpan);
                var offsetDesc = !string.IsNullOrWhiteSpace(offsetStr) ? $" with offset {offsetStr}" : "";
                return new ResolvedTimerInfo(
                    TimerType: "Relative",
                    TargetProperty: targetProp,
                    RawTargetValue: rawValStr,
                    BaseTimestampUtc: baseDt,
                    OffsetStr: offsetStr,
                    OffsetSpan: offsetSpan,
                    ScheduledDueUtc: dueUtc,
                    DurationSpan: null,
                    Description: $"Relative timer scheduled for {dueUtc:O} (target property '{targetProp}' [{baseDt:O}]{offsetDesc})"
                );
            }
            else
            {
                return new ResolvedTimerInfo(
                    TimerType: "RelativeUnresolved",
                    TargetProperty: targetProp,
                    RawTargetValue: rawValStr,
                    BaseTimestampUtc: null,
                    OffsetStr: offsetStr,
                    OffsetSpan: offsetSpan,
                    ScheduledDueUtc: null,
                    DurationSpan: null,
                    Description: $"Relative timer referencing '{targetProp}' (value not found or invalid date in payload)"
                );
            }
        }

        // 3. Static duration string (from conditions or step.Sla?.Duration)
        string? durStr = GetConditionValue(conditions, "duration");
        if (string.IsNullOrWhiteSpace(durStr) && !string.IsNullOrWhiteSpace(step.Sla?.Duration))
        {
            durStr = step.Sla.Duration;
        }

        if (!string.IsNullOrWhiteSpace(durStr))
        {
            var durationSpan = ParseDurationString(durStr);
            return new ResolvedTimerInfo(
                TimerType: "Duration",
                TargetProperty: null,
                RawTargetValue: null,
                BaseTimestampUtc: null,
                OffsetStr: null,
                OffsetSpan: null,
                ScheduledDueUtc: null,
                DurationSpan: durationSpan,
                Description: $"Duration timer set for {durStr} ({durationSpan})"
            );
        }

        return new ResolvedTimerInfo(
            TimerType: "Default",
            TargetProperty: null,
            RawTargetValue: null,
            BaseTimestampUtc: null,
            OffsetStr: null,
            OffsetSpan: null,
            ScheduledDueUtc: null,
            DurationSpan: TimeSpan.FromSeconds(5),
            Description: "Default timer (Duration: configured)"
        );
    }

    private static string? GetConditionValue(Dictionary<string, string> conditions, params string[] keys)
    {
        if (conditions == null || conditions.Count == 0) return null;
        foreach (var key in keys)
        {
            var match = conditions.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (match != null && conditions.TryGetValue(match, out var val) && !string.IsNullOrWhiteSpace(val))
            {
                return val;
            }
        }
        return null;
    }

    private static TimeSpan ParseDurationString(string? durationStr)
    {
        if (string.IsNullOrWhiteSpace(durationStr))
            return TimeSpan.FromSeconds(5);

        durationStr = durationStr.Trim();
        bool isNegative = durationStr.StartsWith("-");
        if (isNegative || durationStr.StartsWith("+"))
        {
            durationStr = durationStr[1..].Trim();
        }

        TimeSpan parsed;
        if (durationStr.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(durationStr[..^1], out var seconds))
        {
            parsed = TimeSpan.FromSeconds(seconds);
        }
        else if (durationStr.EndsWith("m", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(durationStr[..^1], out var minutes))
        {
            parsed = TimeSpan.FromMinutes(minutes);
        }
        else if (durationStr.EndsWith("h", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(durationStr[..^1], out var hours))
        {
            parsed = TimeSpan.FromHours(hours);
        }
        else if (durationStr.EndsWith("d", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(durationStr[..^1], out var days))
        {
            parsed = TimeSpan.FromDays(days);
        }
        else if (double.TryParse(durationStr, out var rawSecs))
        {
            parsed = TimeSpan.FromSeconds(rawSecs);
        }
        else if (durationStr.Contains(':') && TimeSpan.TryParse(durationStr, out var ts))
        {
            parsed = ts;
        }
        else
        {
            try
            {
                parsed = System.Xml.XmlConvert.ToTimeSpan(durationStr);
            }
            catch
            {
                parsed = TimeSpan.FromSeconds(5);
            }
        }

        return isNegative ? -parsed : parsed;
    }
}
