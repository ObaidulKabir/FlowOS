using System;
using System.Linq;
using System.Text.Json; // Added for JSON validation
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Validation;

namespace FlowOS.Domain.Services;

public interface IWorkflowClassValidator
{
    ValidationResult Validate(WorkflowClass workflowClass);
    ValidationResult Validate(WorkflowClassBlueprint blueprint, string name = "DraftWorkflow");
}

public class WorkflowClassValidator : IWorkflowClassValidator
{
    public ValidationResult Validate(WorkflowClassBlueprint blueprint, string name = "DraftWorkflow")
    {
        var wc = new WorkflowClass(Guid.Empty, name, "1.0.0", blueprint);
        return Validate(wc);
    }

    public ValidationResult Validate(WorkflowClass workflowClass)
    {
        var result = new ValidationResult();
        var bp = workflowClass.Definition;

        if (bp == null)
        {
            result.AddError("STR-000", "Structural", "Definition is required", "Metadata");
            return result;
        }

        // 1. Structural Validation
        if (string.IsNullOrWhiteSpace(workflowClass.Name))
            result.AddError("STR-001", "Structural", "Name is required", "Metadata");
        
        if (string.IsNullOrWhiteSpace(workflowClass.Version))
            result.AddError("STR-002", "Structural", "Version is required", "Metadata");

        // NEW: Layer 1 - Strict Workflow Schema Validation
        if (string.IsNullOrWhiteSpace(bp.StateMachine.InitialState))
            result.AddError("WF-STR-001", "WorkflowStructure", "InitialState is required", "StateMachine");

        if (bp.Workflow.Steps == null || !bp.Workflow.Steps.Any())
            result.AddError("WF-STR-002", "WorkflowStructure", "Workflow must have at least one step", "Workflow");

        foreach (var step in bp.Workflow.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.StepId))
                result.AddError("WF-STR-003", "WorkflowStructure", "Step ID cannot be empty", "Workflow");
            
            if (string.IsNullOrWhiteSpace(step.StepType))
                result.AddError("WF-STR-004", "WorkflowStructure", $"StepType is required for step '{step.StepId}'", "Workflow");
        }

        // 2. Internal Consistency
        // Events declared vs used
        var declaredEvents = bp.Events.Select(e => e.EventId).ToHashSet();
        
        // NEW: Validate Event Schemas
        foreach (var evt in bp.Events)
        {
            if (!string.IsNullOrWhiteSpace(evt.PayloadSchema))
            {
                try
                {
                    JsonDocument.Parse(evt.PayloadSchema);
                }
                catch (JsonException)
                {
                    result.AddError("EVT-SCHEMA-001", "Events", $"Event '{evt.EventId}' has invalid JSON Schema", "Events");
                }
            }
        }

        // Check State Machine transitions
        foreach (var t in bp.StateMachine.Transitions)
        {
            if (!declaredEvents.Contains(t.EventId))
                result.AddError("CON-001", "Consistency", $"Transition references undeclared event '{t.EventId}'", "StateMachine");
            
            if (!bp.StateMachine.States.Contains(t.FromState))
                result.AddError("CON-002", "Consistency", $"Transition references unknown FromState '{t.FromState}'", "StateMachine");
            
            if (!bp.StateMachine.States.Contains(t.ToState))
                result.AddError("CON-003", "Consistency", $"Transition references unknown ToState '{t.ToState}'", "StateMachine");
        }

        // NEW: Layer 2 - Workflow Completeness Validation
        var stepIds = bp.Workflow.Steps.Select(s => s.StepId).ToHashSet();

        // 1. Start Step Validation
        // CHANGED: Use Workflow.StartStepId instead of StateMachine.InitialState
        if (string.IsNullOrWhiteSpace(bp.Workflow.StartStepId))
            result.AddError("WF-COMP-000", "WorkflowCompleteness", "Workflow must have a defined StartStepId", "Workflow");
        
        if (!string.IsNullOrEmpty(bp.Workflow.StartStepId) && !stepIds.Contains(bp.Workflow.StartStepId))
             result.AddError("WF-COMP-001", "WorkflowCompleteness", $"StartStepId '{bp.Workflow.StartStepId}' references a non-existent step", "Workflow");

        // 2. Exit Path Validation & Reachability (BFS)
        var reachableSteps = new HashSet<string>();
        if (!string.IsNullOrEmpty(bp.Workflow.StartStepId) && stepIds.Contains(bp.Workflow.StartStepId))
        {
            var queue = new Queue<string>();
            queue.Enqueue(bp.Workflow.StartStepId);
            reachableSteps.Add(bp.Workflow.StartStepId);

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                var step = bp.Workflow.Steps.FirstOrDefault(s => s.StepId == currentId);
                
                if (step == null) continue;

                // Enqueue next steps
                if (step.NextSteps != null)
                {
                    foreach (var next in step.NextSteps.Values)
                    {
                        if (next == "END") continue;
                        if (!reachableSteps.Contains(next) && stepIds.Contains(next))
                        {
                            reachableSteps.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }

                // Enqueue Decision paths
                if (string.Equals(step.StepType, "Decision", StringComparison.OrdinalIgnoreCase) && step.Conditions != null)
                {
                    foreach (var next in step.Conditions.Values)
                    {
                        if (next == "END") continue;
                        if (!reachableSteps.Contains(next) && stepIds.Contains(next))
                        {
                            reachableSteps.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }

                // Enqueue Fork paths
                if (string.Equals(step.StepType, "Fork", StringComparison.OrdinalIgnoreCase))
                {
                    var forkTargets = (step.Branches != null && step.Branches.Any())
                        ? step.Branches
                        : (step.NextSteps != null ? step.NextSteps.Values.ToList() : new List<string>());

                    foreach (var next in forkTargets)
                    {
                        if (next == "END") continue;
                        if (!reachableSteps.Contains(next) && stepIds.Contains(next))
                        {
                            reachableSteps.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }
            }
        }

        // Check Reachability (Orphans)
        foreach (var step in bp.Workflow.Steps)
        {
            if (!string.IsNullOrWhiteSpace(step.StepId) && !reachableSteps.Contains(step.StepId))
                 result.AddError("WF-COMP-004", "WorkflowCompleteness", $"Step '{step.StepId}' is unreachable from start", "Workflow");
                 
            // Check Exit Path (Strict Rule from Prompt)
            bool isEndStep = string.Equals(step.StepType, "End", StringComparison.OrdinalIgnoreCase);
            bool isFork = string.Equals(step.StepType, "Fork", StringComparison.OrdinalIgnoreCase);
            bool isJoin = string.Equals(step.StepType, "Join", StringComparison.OrdinalIgnoreCase);
            bool isDecision = string.Equals(step.StepType, "Decision", StringComparison.OrdinalIgnoreCase);
            bool isCommand = string.Equals(step.StepType, "Command", StringComparison.OrdinalIgnoreCase);
            bool isSystem = string.Equals(step.StepType, "SystemTask", StringComparison.OrdinalIgnoreCase);
            bool isSubWorkflow = string.Equals(step.StepType, "SubWorkflow", StringComparison.OrdinalIgnoreCase);

            if (isEndStep)
            {
                if (step.NextSteps != null && step.NextSteps.Any())
                    result.AddError("WF-STRUCT-005", "WorkflowStructure", $"End Step '{step.StepId}' should not have NextSteps", "Workflow");
            }
            else if (isFork)
            {
                var branches = (step.Branches != null && step.Branches.Any())
                    ? step.Branches
                    : (step.NextSteps != null ? step.NextSteps.Values.ToList() : new List<string>());

                if (branches == null || branches.Distinct().Count() < 2)
                {
                    result.AddError("WF-FORK-001", "WorkflowStructure", $"Fork Step '{step.StepId}' must declare at least 2 distinct target branches", "Workflow");
                }
            }
            else if (isJoin)
            {
                if (step.InboundSteps == null || step.InboundSteps.Distinct().Count() < 2)
                {
                    result.AddError("WF-JOIN-001", "WorkflowStructure", $"Join Step '{step.StepId}' must declare at least 2 distinct InboundSteps", "Workflow");
                }
                if (step.NextSteps == null || !step.NextSteps.Any())
                {
                    result.AddError("WF-COMP-002", "WorkflowCompleteness", $"Join Step '{step.StepId}' has no exit path", "Workflow");
                }
            }
            else if (isDecision)
            {
                if (step.Conditions == null || !step.Conditions.Any())
                    result.AddError("WF-COMP-002", "WorkflowCompleteness", $"Decision Step '{step.StepId}' has no conditions", "Workflow");
                
                // Decisions should ideally have a Default/Else path or cover all cases?
                // Hard to check completeness of logic, but let's check structure.
            }
            else if (isCommand || isSystem || isSubWorkflow)
            {
                // Commands/System Tasks typically have a "Default" transition (auto-advance) or event triggers?
                // Actually, "Command" usually means "Execute Command -> Auto Advance".
                // So it should have NextSteps["Default"] or similar.
                if (step.NextSteps == null || !step.NextSteps.Any())
                    result.AddError("WF-COMP-002", "WorkflowCompleteness", $"Step '{step.StepId}' ({step.StepType}) has no exit path", "Workflow");
            }
            else // HumanTask, Timer, etc.
            {
                if (step.NextSteps == null || !step.NextSteps.Any())
                {
                    result.AddError("WF-COMP-002", "WorkflowCompleteness", $"Step '{step.StepId}' has no exit path", "Workflow");
                }
            }
        }

        // 3. Check Workflow Steps (Consistency)
        foreach (var step in bp.Workflow.Steps)
        {
            // Validate step existence for all transitions
            if (step.NextSteps != null)
            {
                foreach (var nextStepId in step.NextSteps.Values)
                {
                    if (nextStepId != "END" && !stepIds.Contains(nextStepId))
                        result.AddError("CON-004", "Consistency", $"Step '{step.StepId}' references unknown NextStep '{nextStepId}'", "Workflow");
                }

                foreach (var eventKey in step.NextSteps.Keys)
                {
                    bool isImplicitSubWorkflowCompletion =
                        string.Equals(step.StepType, "SubWorkflow", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(eventKey, "SubWorkflowCompleted", StringComparison.OrdinalIgnoreCase);

                    if (eventKey != "Default" && !isImplicitSubWorkflowCompletion && !declaredEvents.Contains(eventKey))
                        result.AddError("CON-005", "Consistency", $"Step '{step.StepId}' references undeclared event '{eventKey}'", "Workflow");
                }
            }

            // Check Decision Conditions targets
            if (string.Equals(step.StepType, "Decision", StringComparison.OrdinalIgnoreCase) && step.Conditions != null)
            {
                foreach (var nextStepId in step.Conditions.Values)
                {
                    if (nextStepId != "END" && !stepIds.Contains(nextStepId))
                        result.AddError("CON-004", "Consistency", $"Step '{step.StepId}' Conditions references unknown NextStep '{nextStepId}'", "Workflow");
                }
            }

            // Check Fork branch targets
            if (string.Equals(step.StepType, "Fork", StringComparison.OrdinalIgnoreCase))
            {
                var forkTargets = (step.Branches != null && step.Branches.Any())
                    ? step.Branches
                    : (step.NextSteps != null ? step.NextSteps.Values.ToList() : new List<string>());

                foreach (var nextStepId in forkTargets)
                {
                    if (nextStepId != "END" && !stepIds.Contains(nextStepId))
                        result.AddError("CON-004", "Consistency", $"Fork Step '{step.StepId}' references unknown branch target '{nextStepId}'", "Workflow");
                }
            }

            // Check Join inbound steps
            if (string.Equals(step.StepType, "Join", StringComparison.OrdinalIgnoreCase) && step.InboundSteps != null)
            {
                foreach (var inboundId in step.InboundSteps)
                {
                    if (!stepIds.Contains(inboundId))
                        result.AddError("CON-004", "Consistency", $"Join Step '{step.StepId}' references unknown InboundStep '{inboundId}'", "Workflow");
                }
            }

            // Check Step SLA validation
            if (step.Sla != null)
            {
                if (string.IsNullOrWhiteSpace(step.Sla.Duration))
                {
                    result.AddError("WF-SLA-001", "WorkflowSla", $"Step '{step.StepId}' defines an SLA without a Duration", "Workflow");
                }
                if (string.IsNullOrWhiteSpace(step.Sla.TimeoutEvent))
                {
                    result.AddError("WF-SLA-002", "WorkflowSla", $"Step '{step.StepId}' defines an SLA without a TimeoutEvent", "Workflow");
                }
                else if (!declaredEvents.Contains(step.Sla.TimeoutEvent))
                {
                    result.AddError("CON-005", "Consistency", $"Step '{step.StepId}' SLA references undeclared timeout event '{step.Sla.TimeoutEvent}'", "Workflow");
                }

                if (!string.IsNullOrEmpty(step.Sla.EscalationStepId) && step.Sla.EscalationStepId != "END" && !stepIds.Contains(step.Sla.EscalationStepId))
                {
                    result.AddError("CON-004", "Consistency", $"Step '{step.StepId}' SLA references unknown EscalationStepId '{step.Sla.EscalationStepId}'", "Workflow");
                }
            }

            if (string.Equals(step.StepType, "SubWorkflow", StringComparison.OrdinalIgnoreCase))
            {
                ValidateSubWorkflowReference(step, result);
            }

            // Check Step Lifecycle Actions (OnEntry / OnExit / OnFailure)
            var entryActions = step.OnEntry != null ? step.OnEntry.AsEnumerable() : Enumerable.Empty<StepActionBlueprint>();
            var exitActions = step.OnExit != null ? step.OnExit.AsEnumerable() : Enumerable.Empty<StepActionBlueprint>();
            var failureActions = step.OnFailure != null ? step.OnFailure.AsEnumerable() : Enumerable.Empty<StepActionBlueprint>();
            var allActions = entryActions.Concat(exitActions).Concat(failureActions);

            // Compensation governance: any reachable, side-effecting step must define OnFailure actions.
            bool isReachable = reachableSteps.Contains(step.StepId);
            bool hasSideEffects = entryActions.Concat(exitActions).Any(a =>
            {
                var t = a.ActionType?.Trim().ToLowerInvariant();
                return t == "webhook" || t == "publishevent" || t == "invokecapability" || IsPluginActionAlias(t);
            });
            bool hasCompensation = failureActions.Any();
            if (isReachable && hasSideEffects && !hasCompensation)
            {
                result.AddError(
                    "WF-COMP-010",
                    "WorkflowCompleteness",
                    $"Step '{step.StepId}' has side-effecting lifecycle actions but no OnFailure compensation hooks.",
                    "Workflow");
            }

            foreach (var action in allActions)
            {
                var actionType = action.ActionType?.Trim().ToLowerInvariant() ?? "";
                if (!IsSupportedActionType(actionType))
                {
                    result.AddError("WF-ACT-001", "ActionValidation", $"Step '{step.StepId}' defines an action with unknown ActionType '{action.ActionType}'. Supported types: Notification, Webhook, PublishEvent, InvokeCapability, or plugin-prefixed alias (plugin:* / plugin.*).", "Steps");
                }

                if (actionType == "webhook")
                {
                    var webhookUrl = !string.IsNullOrWhiteSpace(action.Url) ? action.Url : action.Target;
                    if (string.IsNullOrWhiteSpace(webhookUrl) || !Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                    {
                        result.AddError("WF-ACT-002", "ActionValidation", $"Step '{step.StepId}' defines a Webhook action without a valid absolute HTTP/HTTPS Url", "Steps");
                    }
                }
                else if (actionType == "notification")
                {
                    if (string.IsNullOrWhiteSpace(action.Target) && string.IsNullOrWhiteSpace(action.Template))
                    {
                        result.AddError("WF-ACT-003", "ActionValidation", $"Step '{step.StepId}' defines a Notification action without a Target or Template", "Steps");
                    }
                }
                else if (actionType == "publishevent")
                {
                    if (string.IsNullOrWhiteSpace(action.Target) || !declaredEvents.Contains(action.Target))
                    {
                        result.AddError("WF-ACT-004", "ActionValidation", $"Step '{step.StepId}' defines a PublishEvent action with undeclared event '{action.Target}'", "Steps");
                    }
                }
                else if (actionType == "invokecapability")
                {
                    var capabilityName = !string.IsNullOrWhiteSpace(action.Capability) ? action.Capability : action.Target;
                    if (string.IsNullOrWhiteSpace(capabilityName))
                    {
                        result.AddError("WF-ACT-005", "ActionValidation", $"Step '{step.StepId}' defines InvokeCapability action without a capability name (set Capability or Target).", "Steps");
                    }
                }
            }
        }

        // 3. Law Validation (Workflow cannot bypass State Machine)
        // This is complex static analysis. 
        // Heuristic: If a step triggers an event, is that event valid for the current "State"?
        // Since Workflow Step != Entity State (necessarily), this is hard to prove statically without a mapping.
        // However, we can check that *if* the workflow claims to handle an event, that event exists in the SM.
        // (Already checked in Consistency).
        
        // Check Terminal Events
        foreach (var evt in bp.Events.Where(e => e.IsTerminal))
        {
            // Terminal events should not be used as triggers for non-terminal steps? 
            // Or rather, if an event is terminal, it should lead to "END" or a state that is final.
        }

        // 4. Role & Capability Validation
        // Capabilities declared?
        var declaredCaps = bp.Capabilities.Select(c => c.Code).ToHashSet();
        foreach (var role in bp.Roles)
        {
            foreach (var cap in role.GrantedCapabilities)
            {
                if (!declaredCaps.Contains(cap))
                    result.AddError("GOV-001", "Governance", $"Role '{role.Name}' grants undeclared capability '{cap}'", "Roles");
            }
        }

        // 5. Scope-Specific Validation
        if (workflowClass.Scope == Enums.WorkflowClassScope.Public)
        {
            // No tenant secrets? (Hard to check content, but we check structure)
            // No Policies (Policy definitions are not even in the blueprint, which is good)
        }
        
        // NEW: Step Structure Validation per Type
        foreach (var step in bp.Workflow.Steps)
        {
            if (string.Equals(step.StepType, "Decision", StringComparison.OrdinalIgnoreCase))
            {
                if (step.Conditions == null || !step.Conditions.Any())
                    result.AddError("WF-VAL-001", "StepValidation", $"Decision Step '{step.StepId}' must have at least one condition", "Steps");
            }
            else if (string.Equals(step.StepType, "HumanTask", StringComparison.OrdinalIgnoreCase))
            {
                // Human Tasks should usually have explicit event transitions or a default completion path
                if ((step.NextSteps == null || !step.NextSteps.Any()) && (step.Conditions == null || !step.Conditions.Any()))
                     result.AddError("WF-VAL-002", "StepValidation", $"HumanTask Step '{step.StepId}' must have defined NextSteps", "Steps");
            }
        }

        return result;
    }

    private static void ValidateSubWorkflowReference(StepBlueprint step, ValidationResult result)
    {
        var reference = step.SubWorkflow;
        if (reference == null)
        {
            result.AddError("WF-SUB-001", "StepValidation", $"SubWorkflow step '{step.StepId}' must define a subWorkflow reference block.", "Steps");
            return;
        }

        var hasResolvableTarget =
            reference.WorkflowDefinitionId.HasValue && reference.WorkflowDefinitionId.Value != Guid.Empty
            || reference.WorkflowClassId.HasValue && reference.WorkflowClassId.Value != Guid.Empty
            || !string.IsNullOrWhiteSpace(reference.WorkflowName);

        if (!hasResolvableTarget)
        {
            result.AddError(
                "WF-SUB-002",
                "StepValidation",
                $"SubWorkflow step '{step.StepId}' must set one target reference: workflowDefinitionId, workflowClassId, or workflowName.",
                "Steps");
        }
    }

    private static bool IsSupportedActionType(string actionType) =>
        actionType == "notification" ||
        actionType == "webhook" ||
        actionType == "publishevent" ||
        actionType == "invokecapability" ||
        IsPluginActionAlias(actionType);

    private static bool IsPluginActionAlias(string? actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType)) return false;

        var normalized = actionType.Trim().ToLowerInvariant();
        return normalized.StartsWith("plugin:") || normalized.StartsWith("plugin.");
    }
}
