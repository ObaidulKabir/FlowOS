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

    public SimulationTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<CallToolResult> SimulateWorkflowClass(JObject args)
    {
        try
        {
            WorkflowClassBlueprint? blueprint = null;
            string sourceWorkflowName = "InlineBlueprint";

            // 1. Resolve blueprint either from inline object or via draft/published ID
            var inlineBlueprintToken = args["blueprint"] as JObject;
            if (inlineBlueprintToken != null)
            {
                blueprint = inlineBlueprintToken.ToObject<WorkflowClassBlueprint>();
                if (blueprint == null || blueprint.Workflow?.Steps == null || blueprint.Workflow.Steps.Count == 0)
                {
                    return McpToolResults.Fail("MCP-ARG-001", "Provided inline blueprint is invalid or contains no workflow steps.");
                }
            }
            else
            {
                var idStr = args["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(idStr) || !Guid.TryParse(idStr, out var id))
                {
                    return McpToolResults.Fail("MCP-ARG-001", "Either 'blueprint' object or a valid UUID 'id' must be provided.");
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

            // 2. Parse Context, Roles, Events, and MaxSteps
            var payload = ToPayloadDictionary(args["payload"] as JObject);
            var simulatedRole = args["role"]?.ToString()?.Trim();
            if (string.IsNullOrEmpty(simulatedRole))
                simulatedRole = "User";

            var eventsQueue = new Queue<string>();
            if (args["events"] is JArray eventsArray)
            {
                foreach (var evt in eventsArray)
                {
                    var val = evt?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(val))
                        eventsQueue.Enqueue(val);
                }
            }

            int maxSteps = args["maxSteps"]?.Value<int>() ?? 25;
            if (maxSteps < 1) maxSteps = 1;
            if (maxSteps > 100) maxSteps = 100;

            // 3. Initialize State Machine and Workflow Positions
            var startStepId = !string.IsNullOrWhiteSpace(blueprint.Workflow.StartStepId)
                ? blueprint.Workflow.StartStepId
                : (blueprint.Workflow.Steps.FirstOrDefault()?.StepId ?? "Start");

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
            object? pendingHumanTask = null;

            // Trigger OnEntry for initial step if present
            var initialStepObj = blueprint.Workflow.Steps.FirstOrDefault(s =>
                string.Equals(s.StepId, currentStepId, StringComparison.OrdinalIgnoreCase));
            if (initialStepObj != null)
            {
                EvaluateAndRecordActions(initialStepObj.OnEntry, "OnEntry", initialStepObj.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
            }

            // 4. Execution Loop
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

                var step = blueprint.Workflow.Steps.FirstOrDefault(s =>
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
                            var targetStepObj = blueprint.Workflow.Steps.FirstOrDefault(s =>
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

                // --- HUMAN TASK STEP ---
                if (stepTypeLower.Contains("human"))
                {
                    var roles = GetStepRoles(step);
                    bool authorized = IsRoleAuthorized(roles, simulatedRole);

                    if (!authorized)
                    {
                        status = "WaitingForHumanTask";
                        pendingHumanTask = new
                        {
                            stepId = step.StepId,
                            requiredRoles = roles,
                            allowedEvents = step.NextSteps?.Keys.ToList() ?? new List<string>(),
                            sla = step.Sla,
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

                        // Check State Machine transition
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

                        totalStepsExecuted++;
                        executionTrace.Add(new
                        {
                            stepNumber = totalStepsExecuted,
                            stepId = step.StepId,
                            stepType = "HumanTask",
                            action = $"Fired event '{evt}' with role '{simulatedRole}'. Advanced to '{targetStep}'. State is now '{currentState}'.",
                            state = currentState
                        });

                        EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                        currentStepId = targetStep;
                        if (!string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
                        {
                            var targetStepObj = blueprint.Workflow.Steps.FirstOrDefault(s =>
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
                        // Pauses at Human Task
                        status = "WaitingForHumanTask";
                        pendingHumanTask = new
                        {
                            stepId = step.StepId,
                            requiredRoles = roles,
                            allowedEvents = step.NextSteps?.Keys.ToList() ?? new List<string>(),
                            sla = step.Sla
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

                // --- TIMER STEP ---
                if (stepTypeLower.Contains("timer"))
                {
                    if (eventsQueue.Count > 0)
                    {
                        var evt = eventsQueue.Dequeue();
                        var nextSteps = step.NextSteps ?? new Dictionary<string, string>();
                        var match = nextSteps.FirstOrDefault(kvp => string.Equals(kvp.Key, evt, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(match.Value))
                        {
                            var targetStep = match.Value;
                            var (smTrans, guardBlocked, guardReason) = FindTransition(blueprint.StateMachine, currentState, evt, payload);
                            if (guardBlocked)
                            {
                                status = "BlockedByGuard";
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
                                action = $"Timer step triggered by event '{evt}'. Advanced to '{targetStep}'.",
                                state = currentState
                            });

                            EvaluateAndRecordActions(step.OnExit, "OnExit", step.StepId, payload, actionsTriggered, executionTrace, totalStepsExecuted);
                            currentStepId = targetStep;
                            if (!string.Equals(currentStepId, "END", StringComparison.OrdinalIgnoreCase))
                            {
                                var targetStepObj = blueprint.Workflow.Steps.FirstOrDefault(s =>
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
                    executionTrace.Add(new
                    {
                        stepNumber = totalStepsExecuted + 1,
                        stepId = step.StepId,
                        stepType = "Timer",
                        action = $"Workflow paused at Timer step '{step.StepId}' (Duration: {step.Sla?.Duration ?? "configured"}).",
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

                    // Check State Machine transition if an event was triggered
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
                        var targetStepObj = blueprint.Workflow.Steps.FirstOrDefault(s =>
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

            return McpToolResults.Success(new
            {
                workflow = sourceWorkflowName,
                status,
                initialState,
                finalState = currentState,
                initialStepId = startStepId,
                currentStepId,
                totalStepsExecuted,
                simulatedRole,
                pendingHumanTask,
                decisionsEvaluated,
                stateTransitions,
                actionsTriggered,
                executionTrace,
                payload
            });
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Simulation execution failed: {ex.Message}");
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
                actionsTriggered.Add(new
                {
                    stepId,
                    hook = hookType,
                    actionType = action.ActionType,
                    target = action.Target,
                    condition = action.Condition,
                    status = "Executed"
                });
                executionTrace.Add(new
                {
                    stepNumber,
                    stepId,
                    stepType = "LifecycleAction",
                    action = $"[Hook {hookType}] Executed {action.ActionType} action targeting '{action.Target}'" +
                             (!string.IsNullOrWhiteSpace(action.Condition) ? $" (Condition '{action.Condition}' matched)." : "."),
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
}
