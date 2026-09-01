using FlowOS.Application.Queries.Governance;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using MediatR;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class AnalysisTools
{
    private readonly IMediator _mediator;

    public AnalysisTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    public Task<CallToolResult> ExplainValidationViolation(JObject args)
    {
        var code = args["code"]?.ToString();
        var context = args["context"] as JObject;

        if (string.IsNullOrEmpty(code))
        {
            return Task.FromResult(McpToolResults.Fail("MCP-ARG-001", "code is required."));
        }

        string humanExplanation;
        string designHint;

        switch (code)
        {
            // Structural / metadata
            case "STR-000":
                humanExplanation = "The WorkflowClass definition is null or empty.";
                designHint = "Provide a complete WorkflowClassBlueprint JSON object.";
                break;
            case "STR-001":
                humanExplanation = "Name is required.";
                designHint = "Set a non-empty Name on the WorkflowClass.";
                break;
            case "STR-002":
                humanExplanation = "Version is required.";
                designHint = "Set a SemVer Version string (e.g. 1.0.0).";
                break;

            // Workflow / SM structure
            case "WF-STR-001":
                humanExplanation = "InitialState is required on the StateMachine.";
                designHint = "Set StateMachine.InitialState to one of the States.";
                break;
            case "WF-STR-002":
                humanExplanation = "Workflow must have at least one step.";
                designHint = "Add at least one entry to Workflow.Steps.";
                break;
            case "WF-STR-003":
                humanExplanation = "A step has an empty StepId.";
                designHint = "Give every step a unique non-empty StepId.";
                break;
            case "WF-STR-004":
                humanExplanation = "StepType is required for a step.";
                designHint = $"Set StepType on step '{context?["stepId"]}' (Command, HumanTask, Decision, Timer, or End).";
                break;
            case "WF-STRUCT-005":
                humanExplanation = "An End step must not declare NextSteps.";
                designHint = $"Remove NextSteps from End step '{context?["stepId"]}'.";
                break;

            // Completeness
            case "WF-COMP-000":
                humanExplanation = "Workflow must define StartStepId.";
                designHint = "Set Workflow.StartStepId to an existing StepId.";
                break;
            case "WF-COMP-001":
                humanExplanation = "StartStepId does not match any defined step.";
                designHint = $"Ensure a step with StepId '{context?["stepId"] ?? context?["StartStepId"]}' exists.";
                break;
            case "WF-COMP-002":
                humanExplanation = "A step has no valid exit path (or a Decision has no conditions).";
                designHint = $"Add NextSteps or Conditions for step '{context?["stepId"]}'.";
                break;
            case "WF-COMP-004":
                humanExplanation = "A step is unreachable from StartStepId.";
                designHint = $"Add a path to '{context?["stepId"]}', or remove the dead step.";
                break;

            // Consistency
            case "CON-001":
                humanExplanation = "A state-machine transition references an undeclared event.";
                designHint = $"Add event '{context?["event"] ?? context?["EventId"]}' to Events, or fix the transition EventId.";
                break;
            case "CON-002":
                humanExplanation = "A transition references an unknown FromState.";
                designHint = $"Define state '{context?["state"]}' in States, or fix the typo.";
                break;
            case "CON-003":
                humanExplanation = "A transition references an unknown ToState.";
                designHint = $"Define state '{context?["state"]}' in States, or fix the typo.";
                break;
            case "CON-004":
                humanExplanation = "A step references an unknown NextStep.";
                designHint = $"Ensure target StepId '{context?["stepId"]}' exists in Steps.";
                break;
            case "CON-005":
                humanExplanation = "A step NextSteps key references an undeclared event.";
                designHint = $"Add event '{context?["event"]}' to Events, or fix the NextSteps key.";
                break;

            // Step validation
            case "WF-VAL-001":
                humanExplanation = "A Decision step must have at least one condition.";
                designHint = $"Add Conditions on Decision step '{context?["stepId"]}'.";
                break;
            case "WF-VAL-002":
                humanExplanation = "A HumanTask step must define NextSteps.";
                designHint = $"Add NextSteps on HumanTask '{context?["stepId"]}'.";
                break;

            // SLA / Boundary Timer validation
            case "WF-SLA-001":
                humanExplanation = "A step SLA definition is missing a Duration.";
                designHint = $"Provide a valid Duration (e.g. '24h', '30m', '10s') on step '{context?["stepId"]}'.";
                break;
            case "WF-SLA-002":
                humanExplanation = "A step SLA definition is missing a TimeoutEvent.";
                designHint = $"Specify a declared TimeoutEvent (e.g. 'EVT-TIMEOUT', 'EVT-ESCALATE') on step '{context?["stepId"]}'.";
                break;

            // Events / governance
            case "EVT-SCHEMA-001":
                humanExplanation = "An event has an invalid JSON Schema in PayloadSchema.";
                designHint = $"Fix PayloadSchema for event '{context?["event"] ?? context?["EventId"]}'.";
                break;
            case "GOV-001":
                humanExplanation = "A role grants a capability that is not declared in Capabilities.";
                designHint = $"Declare capability '{context?["capability"]}' under Capabilities, or remove it from the role.";
                break;

            default:
                humanExplanation = code switch
                {
                    _ when code.StartsWith("CON-", StringComparison.Ordinal) => "Consistency violation between Events, StateMachine, and Workflow.",
                    _ when code.StartsWith("WF-COMP-", StringComparison.Ordinal) => "Workflow completeness violation.",
                    _ when code.StartsWith("WF-STR", StringComparison.Ordinal) || code.StartsWith("WF-STRUCT", StringComparison.Ordinal)
                        => "Workflow / state-machine structure violation.",
                    _ when code.StartsWith("STR-", StringComparison.Ordinal) => "Structural / metadata violation.",
                    _ when code.StartsWith("GOV-", StringComparison.Ordinal) => "Governance (roles/capabilities) violation.",
                    _ => "Unknown violation code."
                };
                designHint = "See the validator message and Chapter 9 validation rules.";
                break;
        }

        return Task.FromResult(McpToolResults.Success(new { code, humanExplanation, designHint }));
    }

    public async Task<CallToolResult> LintDraftWorkflowClass(JObject args)
    {
        var idStr = args["id"]?.ToString();
        if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id))
        {
            return McpToolResults.Fail("MCP-ARG-002", "id must be a valid UUID.");
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

        FlowOS.Application.DTOs.Governance.WorkflowClassResponseDto? workflowClass;
        try
        {
            workflowClass = await _mediator.Send(new GetWorkflowClassByIdQuery(tenantId, id));
        }
        catch (UnauthorizedAccessException)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found.");
        }
        if (workflowClass == null)
            return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found.");

        var warnings = new List<object>();

        var definedEvents = workflowClass.Definition.Events.Select(e => e.EventId).ToHashSet();
        var usedEvents = new HashSet<string>();

        if (workflowClass.Definition.StateMachine?.Transitions != null)
        {
            foreach (var t in workflowClass.Definition.StateMachine.Transitions)
                usedEvents.Add(t.EventId);
        }

        if (workflowClass.Definition.Workflow?.Steps != null)
        {
            foreach (var s in workflowClass.Definition.Workflow.Steps)
            {
                if (s.NextSteps == null) continue;
                foreach (var evt in s.NextSteps.Keys)
                    usedEvents.Add(evt);
            }
        }

        foreach (var evt in definedEvents)
        {
            if (!usedEvents.Contains(evt))
            {
                warnings.Add(new
                {
                    code = "LINT-EVT-001",
                    severity = "Warning",
                    message = $"Event '{evt}' is defined but never used in StateMachine or Workflow.",
                    context = new { eventId = evt }
                });
            }
        }

        if (workflowClass.Definition.StateMachine?.States?.Count > 15)
        {
            warnings.Add(new
            {
                code = "LINT-CMP-001",
                severity = "Info",
                message = "State machine has over 15 states. Consider splitting into sub-workflows.",
                context = new { stateCount = workflowClass.Definition.StateMachine.States.Count }
            });
        }

        if (workflowClass.Definition.Workflow?.Steps != null)
        {
            foreach (var step in workflowClass.Definition.Workflow.Steps)
            {
                if (string.IsNullOrWhiteSpace(step.StepId) || step.StepId.Length < 3)
                {
                    warnings.Add(new
                    {
                        code = "LINT-QLT-001",
                        severity = "Warning",
                        message = $"Step '{step.StepId}' has a too short identifier.",
                        context = new { stepId = step.StepId }
                    });
                }
            }
        }

        return McpToolResults.Success(new { warnings });
    }
}
