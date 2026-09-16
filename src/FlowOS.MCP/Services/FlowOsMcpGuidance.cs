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

        Connection contract (read this before the first JSON-RPC POST):
        - Use the absolute `url` from GET discovery / GET /.well-known/mcp exactly.
        - On https://flowosbd.com the JSON-RPC path is `/mcp/` (trailing slash required). On https://flowos.prospectbdltd.com it is `/mcp`.
        - Never strip a trailing slash. Never follow HTTP 301/302 for POST; a slash redirect can switch to http:// and drop the body and API key.
        - Accept: application/json, text/event-stream. Headers: X-MCP-API-Key (or Authorization: Bearer) and x-tenant-id.
        - Keys are issued per host. Production keys only work on flowosbd.com; staging keys only work on flowos.prospectbdltd.com.
        - Commercial policy: MCP is included in an active Managed Cloud or Enterprise tenant subscription. Trial keys may discover, lint, validate, and simulate. Runtime tools (start_workflow, publish_event, complete_task, publish, activate) require a paid plan and return MCP-PLAN-REQUIRED until activated.

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
          • To reuse one template in multiple business domains, call `create_context_binding` against the draft or published class,
            then `simulate_context_binding` with revision `draft`. Do not publish a throwaway simulator variant first.
            `validate_context_binding` and `activate_context_binding` require a Published or Public source.
            Simulation does not require tenant roles to exist; activation does (CTX-ROLE-002).
          • Call `simulate_context_binding` with revision `draft` before activation, then with `active`
            to verify the exact pinned runtime. Simulation never dispatches or persists side effects.
          • Activation and archival require explicit human confirmation. Bindings never create roles or permissions.

        [Step 4: Instantiate Runtime Workflow]
          • Call `start_workflow` with exactly one selector: `workflowClassId`, `workflowName`,
            `workflowDefinitionId`, `contextBindingId`, or `contextType`.
          • The response contains `workflowInstanceId`, starting step, and active state.

        [Step 5: Drive Workflow Transitions & Inspect Telemetry]
          • Call `publish_event` with `workflowInstanceId` and `eventType` to trigger state transitions (e.g. EVT-SUBMIT).
          • Call `complete_task` with `workflowInstanceId` and `taskId` to complete human/manual tasks.
          • Call `get_workflow_instance_status` or `list_workflow_instances` to inspect runtime status, current step, and execution history.
          • Call `suggest_agent_action` to run AI risk analysis or decision advisory on active instances.

        Tip: Call MCP Prompts (`prompts/list` & `prompts/get`) or read MCP Resources (`resources/list` & `resources/read`) for full templates.
          Preferred prompt: `design_dual_kernel_workflow`. Preferred resource: `flowos://guides/dual-kernel-design`.

        Dual-kernel design law (read before drafting Decision steps):
        - The workflow graph moves `currentStep`. The state machine moves `currentState`. They are independent kernels.
        - A Decision with `conditions.Default` or `conditions.true` auto-routes the workflow WITHOUT consuming a business event.
        - If the state machine still requires that event (e.g. ApproveQuote auto-routes to MaterialDecision while Assigned → Quoted needs QUOTE_APPROVED), you MUST still send the event in `simulate_workflowclass` / `simulate_context_binding` / `publish_event`.
        - FlowOS then applies it as a state-only catch-up: step stays put, state advances. Omitting it leaves state behind; the next event is Denied as a state-machine violation.
        - Preferred design: HumanTask/Command `nextSteps` consume the same event the state machine uses. Do not auto-skip a legal gate unless you still emit that event.
        - Context bindings do not create tenant roles. `simulate_context_binding` may use a Draft template and does not require tenant roles to exist. Do not publish a stripped-roles copy just to simulate. `validate_context_binding` / `activate_context_binding` still need a Published source and real tenant roles (CTX-ROLE-002).
        - Diagnose divergence: if `currentStep` is ahead of `currentState` (e.g. MaterialDecision / Assigned), the missing event is the unused state-machine trigger.
        - SLA reminders do not need a live waiting instance. `simulate_workflowclass` / `simulate_context_binding` inject reminder triggerEvents in duration order before a completing nextSteps event (e.g. QUOTE_APPROVED). Put TimeoutEvent (QUOTE_RESPONSE_OVERDUE) on nextSteps. Set autoAdvanceTimers=true and omit the completing event to fire the timeout. Trace lines contain `[SLA Reminder Fired]` / `[SLA Timeout Fired]`.
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
                name = "design_dual_kernel_workflow",
                description = "How to design workflow steps and state-machine transitions together, including Decision auto-route vs required business events, context-binding roles, and simulate_context_binding.",
                arguments = new[]
                {
                    new { name = "domain", description = "Business process to design (e.g., ServiceRepair, ExpenseApproval)", required = false }
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
        var domain = arguments?["domain"]?.ToString() ?? "ServiceRepair";

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
                                   - State transitions must be declared in `stateMachine.transitions` with `fromState`, `toState`, and `triggerEvent`/`eventId`.
                                   - All trigger event names must be declared in the `events` array.
                                   - Decision `Default`/`true` auto-routes the workflow graph only. If the state machine still needs a business event, send that event anyway (state-only catch-up). Preferred: HumanTask nextSteps consume the same event.
                                3. Submit the blueprint via `create_draft_workflowclass`.
                                4. Verify with `validate_draft_workflowclass` then `simulate_workflowclass` with the full event list including state-machine triggers.
                                5. Publish with `publish_workflowclass`.
                                6. For a tenant business payload, `create_context_binding` → `validate_context_binding` → `simulate_context_binding`. Read `flowos://guides/dual-kernel-design`.
                                """
                        }
                    }
                }
            },

            "design_dual_kernel_workflow" => new
            {
                description = "Dual-kernel design recipe for workflow graph vs state machine",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new
                        {
                            type = "text",
                            text = DualKernelDesignGuide.Replace("{DOMAIN}", domain)
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
                                   - If currentStep is ahead of currentState, a Decision auto-route skipped a gate: publish the unused state-machine event (state-only catch-up). Read `flowos://guides/dual-kernel-design`.
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
                uri = "flowos://guides/dual-kernel-design",
                name = "Dual-Kernel Design Guide",
                description = "How agents must design workflow steps and state-machine events together, including Decision auto-route, state-only catch-up, and simulate_context_binding.",
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

            "flowos://guides/dual-kernel-design" => new
            {
                contents = new[]
                {
                    new
                    {
                        uri,
                        mimeType = "text/markdown",
                        text = DualKernelDesignGuide.Replace("{DOMAIN}", "ServiceRepair")
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

    public const string DualKernelDesignGuide =
        """
        # FlowOS Dual-Kernel Design Guide for AI Agents

        Domain you are designing: {DOMAIN}

        FlowOS has two kernels. Design both, or simulation and runtime will diverge.

        | Kernel | Owns | Moves |
        |---|---|---|
        | Workflow graph | `workflow.steps`, `nextSteps`, Decision `conditions` | `currentStep` |
        | State machine | `stateMachine.transitions` | `currentState` |

        An event may do one of three things:
        1. Advance both (HumanTask/Command `nextSteps` key matches a state-machine `eventId`).
        2. Advance workflow only (Decision `Default` / `true` auto-route). The state machine does not move.
        3. Advance state only (no `nextSteps` on the current step, but a legal transition exists from `currentState`). FlowOS applies this as state-only catch-up.

        ## The ServiceRepair failure mode (canonical)

        Bad pairing:
        - `ApproveQuote` is a Decision with `conditions: { "Default": "MaterialDecision" }` or `{ "true": "MaterialDecision" }`.
        - State machine still has `Assigned + QUOTE_APPROVED → Quoted`.
        - After `JOB_REQUESTED`, step is already `MaterialDecision` while state is still `Assigned`.
        - `MATERIALS_REQUIRED` is then Denied: "Event is not valid for current state 'Assigned'".

        Correct agent behavior:
        1. Prefer making `ApproveQuote` a HumanTask whose `nextSteps.QUOTE_APPROVED` points at `MaterialDecision`.
        2. If you keep the auto-route Decision, still include `QUOTE_APPROVED` in `simulate_workflowclass`, `simulate_context_binding`, and later `publish_event`. FlowOS keeps the step and sets state to `Quoted`.
        3. Never omit the state-machine event to "match" the auto-skip. The skip is workflow-only.

        Diagnose: if trace shows `currentStep` ahead of `currentState` (MaterialDecision / Assigned), look up the unused transition from that state and send that event next.

        ## Preferred MCP design loop

        1. `describe_workflowclass_schema`
        2. `create_draft_workflowclass` — declare `events`, `stateMachine.transitions`, and `workflow.steps` together.
        3. `validate_draft_workflowclass` then `lint_draft_workflowclass`
        4. `simulate_workflowclass` with the **full** event list, including every state-machine trigger, even after Decision auto-routes. SLA reminder events may be omitted: they fire automatically before a completing nextSteps event. Use `autoAdvanceTimers: true` without the completing event to fire TimeoutEvent (do not start a live instance just to prove reminders).
        5. Tenant context (do not strip roles or publish a throwaway no-roles variant):
           - `create_context_binding` against the **draft** template id, with `inputMapping` for canonical fields
           - `simulate_context_binding` with `revision: "draft"`, a real business `initialPayload`, optional `roles` for the trace, the same full event list, and `autoAdvanceTimers: true` when you need SLA timeout as well as reminders
           - CTX-ROLE-002 applies to `validate_context_binding` / `activate_context_binding`, not to simulation
        6. `publish_workflowclass` with `confirmHumanApproval: true` when required, then `validate_context_binding`
        7. `activate_context_binding` only after draft simulation is Allowed through the expected final state
        8. Runtime: `start_workflow` then `publish_event` for each remaining state-machine trigger

        ## Context-binding rules

        - Bindings never create roles or permissions.
        - Unknown tenant role names fail `validate_context_binding` and `activate_context_binding` (CTX-ROLE-002). They do not fail `simulate_context_binding`.
        - Do not publish a stripped-roles copy of the template just to bind and simulate. Bind the draft, simulate, then publish once.
        - `simulate_context_binding` never persists instances or snapshots. A Denied trace is a design signal, not a reason to delete the template.

        ## Tool names to use

        - Design sandbox: `simulate_workflowclass`
        - Bound business payload: `simulate_context_binding`
        - After a live instance exists: `fork_workflow_simulation` / `replay_workflow_history`

        ## SLA reminders (no live clock required)

        Put `sla.duration`, `sla.timeoutEvent`, and `sla.reminders[]` on the waiting HumanTask or Command. Declare reminder and timeout event IDs. Put TimeoutEvent on `nextSteps` (and a state-machine transition if state should change). Reminder events may loop back, be state-only, or be notification-only.

        Happy path: send the completing event (`QUOTE_APPROVED`). The simulator injects `QUOTE_REMINDER_SENT` at 2h then 12h before approval. Overdue path: `autoAdvanceTimers: true` and omit the completing event so `QUOTE_RESPONSE_OVERDUE` fires after the reminders. Do not start a live instance just to prove the clock.
        """;

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
