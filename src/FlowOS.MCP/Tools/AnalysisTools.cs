using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using System.Collections.Generic;

namespace FlowOS.MCP.Tools
{
    public class AnalysisTools
    {
        private readonly FlowOSDbContext _dbContext;
        private readonly WorkflowClassValidator _validator;

        public AnalysisTools(FlowOSDbContext dbContext, WorkflowClassValidator validator)
        {
            _dbContext = dbContext;
            _validator = validator;
        }

        public Task<CallToolResult> ExplainValidationViolation(JObject args)
        {
            var code = args["code"]?.ToString();
            var context = args["context"] as JObject;

            if (string.IsNullOrEmpty(code))
            {
                return Task.FromResult(new CallToolResult
                {
                    IsError = true,
                    Content = new List<ToolContent> { new ToolContent { Text = "Error code is required." } }
                });
            }

            string humanExplanation = "Unknown violation code.";
            string designHint = "Review the validation rules.";

            // This knowledge base should eventually be moved to a resource file or database
            switch (code)
            {
                // Structural
                case "VAL-001":
                    humanExplanation = "The WorkflowClass definition is null or empty.";
                    designHint = "Ensure the blueprint is properly structured JSON.";
                    break;
                case "VAL-002":
                    humanExplanation = "The StateMachine definition is missing.";
                    designHint = "Define a StateMachine with at least an InitialState and one State.";
                    break;
                case "VAL-003":
                    humanExplanation = "The Workflow definition is missing.";
                    designHint = "Define a Workflow with a valid StartStepId and Steps list.";
                    break;

                // State Machine
                case "VAL-SM-001":
                    humanExplanation = "The InitialState is not listed in the States array.";
                    designHint = $"Add '{context?["state"]}' to the States list.";
                    break;
                case "VAL-SM-002":
                    humanExplanation = "A transition references a FromState that does not exist.";
                    designHint = $"Define the state '{context?["state"]}' in the States list or fix the typo.";
                    break;
                case "VAL-SM-003":
                    humanExplanation = "A transition references a ToState that does not exist.";
                    designHint = $"Define the state '{context?["state"]}' in the States list or fix the typo.";
                    break;
                case "VAL-SM-004":
                    humanExplanation = "A transition references an EventType that is not defined in the Events list.";
                    designHint = $"Add event '{context?["event"]}' to the Events array.";
                    break;

                // Workflow
                case "VAL-WF-001":
                    humanExplanation = "The StartStepId does not match any defined Step.";
                    designHint = $"Ensure a step with StepId '{context?["stepId"]}' exists in the Steps array.";
                    break;
                case "VAL-WF-002":
                    humanExplanation = "A step references a NextStep that does not exist.";
                    designHint = $"Ensure the target StepId '{context?["stepId"]}' is defined.";
                    break;
                case "VAL-WF-003":
                    humanExplanation = "A step references an EventType that is not defined.";
                    designHint = $"Add event '{context?["event"]}' to the Events array.";
                    break;
                case "VAL-WF-005": // Assuming code for unreachable step
                    humanExplanation = "The step cannot be reached from the StartStepId.";
                    designHint = $"Add a transition leading to '{context?["stepId"]}', or remove the dead code.";
                    break;
                
                default:
                    if (code.StartsWith("VAL-SM")) humanExplanation = "State Machine logic violation.";
                    if (code.StartsWith("VAL-WF")) humanExplanation = "Workflow graph violation.";
                    break;
            }

            var result = new
            {
                code = code,
                humanExplanation = humanExplanation,
                designHint = designHint
            };

            return Task.FromResult(new CallToolResult
            {
                Content = new List<ToolContent>
                {
                    new ToolContent { Type = "json", Text = JObject.FromObject(result).ToString() }
                }
            });
        }

        public async Task<CallToolResult> LintDraftWorkflowClass(JObject args)
        {
            var idStr = args["id"]?.ToString();
            if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id))
            {
                 return new CallToolResult 
                 { 
                     IsError = true, 
                     Content = new List<ToolContent> { new ToolContent { Text = "Valid Draft ID is required." } } 
                 };
            }

            var workflowClass = await _dbContext.WorkflowClasses.FindAsync(id);
            if (workflowClass == null) 
            {
                 return new CallToolResult 
                 { 
                     IsError = true, 
                     Content = new List<ToolContent> { new ToolContent { Text = "WorkflowClass not found." } } 
                 };
            }

            var warnings = new List<object>();

            // 1. Check for Orphaned Events (Defined but not used in SM or WF)
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
                    if (s.NextSteps != null)
                    {
                        foreach (var evt in s.NextSteps.Keys)
                            usedEvents.Add(evt);
                    }
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
                        context = new { eventType = evt }
                    });
                }
            }

            // 2. Check for Complexity (Too many states)
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

            // 3. Check for Descriptions (Quality)
            // Note: StepBlueprint does not have Label currently, so we check ID length for now as a proxy for descriptiveness
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

            return new CallToolResult
            {
                Content = new List<ToolContent>
                {
                    new ToolContent { Type = "json", Text = JObject.FromObject(new { warnings = warnings }).ToString() }
                }
            };
        }
    }
}
