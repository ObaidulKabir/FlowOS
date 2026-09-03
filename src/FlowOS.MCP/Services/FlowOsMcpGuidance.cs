using System.Collections.Generic;
using FlowOS.MCP.Models;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Services;

public static class FlowOsMcpGuidance
{
    public const string SystemInstructions =
        """
        FlowOS Process Operating System — Autonomous Agent Operating Guide
        ===================================================================

        FlowOS is a dual-kernel enterprise process operating system that strictly separates:
        1. State Authority (Mathematical State Machine) - Controls what state transitions are legally permitted.
        2. Process Orchestration (Workflow Engine) - Manages step execution, timer SLAs, and task completion.
        3. Policy Governance (RBAC & Capabilities) - Governs who can trigger events or execute steps.

        Canonical 5-Step Operating Lifecycle for AI Agents:
        ---------------------------------------------------
        Follow this exact sequence to operate any business workflow in FlowOS:

        [Step 1: Inspect Schema & Draft Blueprint]
          • Call `describe_workflowclass_schema` to inspect the canonical JSON blueprint structure.
          • Call `create_draft_workflowclass` with `name`, `version`, and `blueprint`.
            The blueprint MUST define:
            - `stateMachine`: `initialState`, `states`, and `transitions` (fromState, toState, triggerEvent).
            - `workflow`: `startStepId` and `steps` (stepId, stepType, requiredRoles, nextSteps).
            - `events`: List of event identifiers (e.g. EVT-SUBMIT, EVT-APPROVE, EVT-REJECT).
            - `roles`: Role definitions governing step permissions.

        [Step 2: Authoritative Parity Validation]
          • Call `validate_draft_workflowclass` with `id`.
          • Verify that `data.isValid` is true.
          • If errors occur, call `explain_validation_violation` with the error code (e.g. CON-001) to get fix hints.
          • Update the draft using `update_draft_workflowclass` until valid.

        [Step 3: Publish Blueprint]
          • Call `publish_workflowclass` with `id`.
          • This freezes the blueprint into a versioned runtime WorkflowDefinition and registers all state transitions.

        [Step 4: Instantiate Runtime Workflow]
          • Call `start_workflow` with `workflowClassId` (or `workflowName`).
          • The response contains `workflowInstanceId`, starting step, and active state.

        [Step 5: Drive Workflow Transitions & Inspect Telemetry]
          • Call `publish_event` with `workflowInstanceId` and `eventType` to trigger state transitions (e.g. EVT-SUBMIT).
          • Call `complete_task` with `workflowInstanceId` and `taskId` to complete human/manual tasks.
          • Call `get_workflow_instance_status` or `list_workflow_instances` to inspect runtime status, current step, and execution history.
          • Call `suggest_agent_action` to run AI risk analysis or decision advisory on active instances.

        Tip: Call MCP Prompts (`prompts/list` & `prompts/get`) or read MCP Resources (`resources/list` & `resources/read`) for full templates.
        """;

    public static object GetPromptsList() => new
    {
        prompts = new[]
        {
            new
            {
                name = "operate_workflow_process",
                description = "Complete end-to-end guide: draft a WorkflowClass blueprint, validate dual-kernel parity, publish it, start an instance, and drive state transitions using events.",
                arguments = new[]
                {
                    new { name = "workflowName", description = "Name of the workflow to build (e.g., ExpenseApproval, StudentLeave, LoanApplication)", required = false }
                }
            },
            new
            {
                name = "draft_and_publish_workflow",
                description = "Guidance on designing and publishing a dual-kernel compliant workflow blueprint with events, state machine, and workflow steps.",
                arguments = new[]
                {
                    new { name = "domain", description = "Target business domain (e.g., Finance, HR, Logistics, Procurement)", required = false }
                }
            },
            new
            {
                name = "run_workflow_instance",
                description = "Guidance on starting a live execution instance and advancing steps using events and task completions.",
                arguments = new[]
                {
                    new { name = "workflowClassId", description = "UUID of the published WorkflowClass to instantiate", required = false }
                }
            },
            new
            {
                name = "troubleshoot_workflow_instance",
                description = "Inspect an existing workflow instance, diagnose state machine violations, and resume execution.",
                arguments = new[]
                {
                    new { name = "instanceId", description = "UUID of the workflow instance to diagnose", required = true }
                }
            }
        }
    };

    public static object? GetPrompt(string name, JObject? arguments)
    {
        var workflowName = arguments?["workflowName"]?.ToString() ?? "ExpenseApprovalV2";
        var workflowClassId = arguments?["workflowClassId"]?.ToString() ?? "<workflowClassId>";
        var instanceId = arguments?["instanceId"]?.ToString() ?? "<instanceId>";

        return name switch
        {
            "operate_workflow_process" => new
            {
                description = "End-to-End Workflow Process Operating Recipe",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new
                        {
                            type = "text",
                            text =
                                """
                                You are operating FlowOS, an enterprise dual-kernel process operating system.
                                Follow this exact 5-step operational pipeline to create and run the workflow '{WORKFLOW_NAME}':

                                1. DRAFT THE BLUEPRINT:
                                   Call tool `create_draft_workflowclass` with:
                                   {
                                     "name": "{WORKFLOW_NAME}",
                                     "version": "1.0.0",
                                     "blueprint": {BLUEPRINT_JSON}
                                   }

                                2. VALIDATE THE DRAFT:
                                   Call `validate_draft_workflowclass` with {"id": "<draft_id>"}.
                                   Ensure `isValid == true`. If validation issues are returned, inspect them with `explain_validation_violation`.

                                3. PUBLISH THE WORKFLOW:
                                   Call `publish_workflowclass` with {"id": "<draft_id>"}.
                                   This transitions the class from Draft to Published and registers its runtime definition.

                                4. START A LIVE INSTANCE:
                                   Call `start_workflow` with:
                                   {
                                     "workflowClassId": "<draft_id>"
                                   }
                                   Save the returned `workflowInstanceId`.

                                5. DRIVE THE WORKFLOW PROCESS VIA EVENTS:
                                   • To submit: Call `publish_event` with:
                                     {
                                       "workflowInstanceId": "<workflowInstanceId>",
                                       "eventType": "EVT-SUBMIT",
                                       "payload": { "amount": 250.0, "vendor": "Office Supplies" }
                                     }
                                   • To approve: Call `publish_event` with:
                                     {
                                       "workflowInstanceId": "<workflowInstanceId>",
                                       "eventType": "EVT-APPROVE-MANAGER",
                                       "payload": { "approvedBy": "Jane Doe", "notes": "Approved" }
                                     }
                                   • Check current status at any time: Call `get_workflow_instance_status` with {"instanceId": "<workflowInstanceId>"}.
                                """
                                .Replace("{WORKFLOW_NAME}", workflowName)
                                .Replace("{BLUEPRINT_JSON}", ReferenceExpenseApprovalJson)
                        }
                    }
                }
            },

            "draft_and_publish_workflow" => new
            {
                description = "Workflow Blueprint Design and Publishing Guide",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new
                        {
                            type = "text",
                            text =
                                """
                                How to Design and Publish a FlowOS WorkflowClass:
                                1. Call `describe_workflowclass_schema` to see all valid JSON schema properties.
                                2. Ensure Dual-Kernel Parity:
                                   - Every step in `workflow.steps` must correspond to a legal state in `stateMachine.states`.
                                   - State transitions must be declared in `stateMachine.transitions` with `fromState`, `toState`, and `triggerEvent`.
                                   - All `triggerEvent` names must be declared in the `events` array.
                                3. Submit the blueprint via `create_draft_workflowclass`.
                                4. Verify with `validate_draft_workflowclass`.
                                5. Publish with `publish_workflowclass`.
                                """
                        }
                    }
                }
            },

            "run_workflow_instance" => new
            {
                description = "Runtime Instance Execution Guide",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new
                        {
                            type = "text",
                            text =
                                """
                                To run an instance of workflow {WORKFLOW_CLASS_ID}:
                                1. Call `start_workflow` passing {"workflowClassId": "{WORKFLOW_CLASS_ID}"}.
                                2. Receive the `workflowInstanceId`.
                                3. Publish business events (`publish_event`) to trigger state transitions according to the state machine graph.
                                4. Complete any assigned tasks with `complete_task`.
                                5. Inspect status and telemetry with `get_workflow_instance_status`.
                                """
                                .Replace("{WORKFLOW_CLASS_ID}", workflowClassId)
                        }
                    }
                }
            },

            "troubleshoot_workflow_instance" => new
            {
                description = "Workflow Instance Troubleshooting Guide",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new
                        {
                            type = "text",
                            text =
                                """
                                Troubleshooting Workflow Instance {INSTANCE_ID}:
                                1. Call `get_workflow_instance_status` with {"instanceId": "{INSTANCE_ID}"}.
                                2. Inspect `currentState`, `currentStep`, and status.
                                3. If an event was rejected, verify:
                                   - Is the event declared for this workflow?
                                   - Does a transition exist from `currentState` using this event in the State Machine?
                                   - Does the caller have the required role or capability?
                                4. Call `suggest_agent_action` with `agentId: "RiskAnalysisAgent"` to analyze anomaly conditions.
                                """
                                .Replace("{INSTANCE_ID}", instanceId)
                        }
                    }
                }
            },

            _ => null
        };
    }

    public static object GetResourcesList() => new
    {
        resources = new[]
        {
            new
            {
                uri = "flowos://guides/lifecycle",
                name = "FlowOS Workflow Operating Lifecycle Guide",
                description = "Complete markdown reference manual on drafting, validating, publishing, and executing workflows.",
                mimeType = "text/markdown"
            },
            new
            {
                uri = "flowos://templates/expense-approval",
                name = "Reference Blueprint: Expense Approval",
                description = "Canonical declarative JSON blueprint featuring a 4-state dual-kernel approval process with roles and events.",
                mimeType = "application/json"
            },
            new
            {
                uri = "flowos://templates/schema-guide",
                name = "WorkflowClass JSON Schema Specification",
                description = "Detailed JSON schema specification and parity rules for authoring valid FlowOS blueprints.",
                mimeType = "application/json"
            }
        }
    };

    public static object? GetResource(string uri)
    {
        return uri switch
        {
            "flowos://guides/lifecycle" => new
            {
                contents = new[]
                {
                    new
                    {
                        uri,
                        mimeType = "text/markdown",
                        text = SystemInstructions
                    }
                }
            },

            "flowos://templates/expense-approval" => new
            {
                contents = new[]
                {
                    new
                    {
                        uri,
                        mimeType = "application/json",
                        text = ReferenceExpenseApprovalJson
                    }
                }
            },

            "flowos://templates/schema-guide" => new
            {
                contents = new[]
                {
                    new
                    {
                        uri,
                        mimeType = "application/json",
                        text = McpToolSchemas.BlueprintSchema().ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                }
            },

            _ => null
        };
    }

    public const string ReferenceExpenseApprovalJson =
        """
        {
          "events": [
            { "id": "EVT-SUBMIT", "name": "Submit Expense", "category": "Human", "description": "Employee submits expense claim" },
            { "id": "EVT-APPROVE-MANAGER", "name": "Manager Approval", "category": "Decision", "description": "Department manager approves claim" },
            { "id": "EVT-REJECT", "name": "Reject Claim", "category": "Decision", "description": "Claim rejected" }
          ],
          "stateMachine": {
            "initialState": "Draft",
            "states": ["Draft", "Submitted", "Approved", "Rejected"],
            "transitions": [
              { "fromState": "Draft", "toState": "Submitted", "triggerEvent": "EVT-SUBMIT" },
              { "fromState": "Submitted", "toState": "Approved", "triggerEvent": "EVT-APPROVE-MANAGER" },
              { "fromState": "Submitted", "toState": "Rejected", "triggerEvent": "EVT-REJECT" }
            ]
          },
          "workflow": {
            "startStepId": "DraftStep",
            "steps": [
              {
                "stepId": "DraftStep",
                "stepType": "HumanTask",
                "requiredRoles": ["Employee"],
                "nextSteps": { "EVT-SUBMIT": "ManagerReviewStep" }
              },
              {
                "stepId": "ManagerReviewStep",
                "stepType": "Decision",
                "requiredRoles": ["Manager"],
                "nextSteps": {
                  "EVT-APPROVE-MANAGER": "ApprovedEndStep",
                  "EVT-REJECT": "RejectedEndStep"
                }
              },
              {
                "stepId": "ApprovedEndStep",
                "stepType": "End"
              },
              {
                "stepId": "RejectedEndStep",
                "stepType": "End"
              }
            ]
          },
          "roles": [
            { "name": "Employee", "description": "Submits expense claims", "grantedCapabilities": ["workflow.create"] },
            { "name": "Manager", "description": "Reviews and approves claims", "grantedCapabilities": ["event.publish"] }
          ],
          "capabilities": [
            { "code": "workflow.create", "description": "Can create workflow drafts" },
            { "code": "event.publish", "description": "Can publish events" }
          ]
        }
        """;
}
