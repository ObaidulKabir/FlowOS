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
          For SLA reminders/timeouts: prompt `test_sla_reminders_in_simulator` and resource `flowos://guides/sla-reminder-simulation`.

        Dual-kernel design law (read before drafting Decision steps):
        - The workflow graph moves `currentStep`. The state machine moves `currentState`. They are independent kernels.
        - A Decision with `conditions.Default` or `conditions.true` auto-routes the workflow WITHOUT consuming a business event.
        - If the state machine still requires that event (e.g. ApproveQuote auto-routes to MaterialDecision while Assigned → Quoted needs QUOTE_APPROVED), you MUST still send the event in `simulate_workflowclass` / `simulate_context_binding` / `publish_event`.
        - FlowOS then applies it as a state-only catch-up: step stays put, state advances. Omitting it leaves state behind; the next event is Denied as a state-machine violation.
        - Preferred design: HumanTask/Command `nextSteps` consume the same event the state machine uses. Do not auto-skip a legal gate unless you still emit that event.
        - Repeatable paths (retry-password, resubmit, pin re-entry) MUST declare `pathLimits` on the looping nextSteps key: `{ "maxTravels": 3, "onExceeded": "LockedOut" }`. The engine counts each travel and fails closed or routes to onExceeded. Cyclic edges without a declaration still cap at 5.
        - Context bindings do not create tenant roles. `simulate_context_binding` may use a Draft template and does not require tenant roles to exist. Do not publish a stripped-roles copy just to simulate. `validate_context_binding` / `activate_context_binding` still need a Published source and real tenant roles (CTX-ROLE-002).
        - Diagnose divergence: if `currentStep` is ahead of `currentState` (e.g. MaterialDecision / Assigned), the missing event is the unused state-machine trigger.

        SLA reminder / timeout simulation law (read before concluding the simulator is broken):
        - `validate_draft_workflowclass` only proves the SLA JSON is legal. It does not fire reminders.
        - A full-path simulate to Paid is a business-event executor, not a wall clock. If `QUOTE_APPROVED` is in `events`, timeout MUST NOT fire (the human responded in time). That is success, not a missing timer.
        - Do not start a live instance and wait. Use `simulate_workflowclass` / `simulate_context_binding`.
        - Happy path: put the completing `nextSteps` event in `events` (`QUOTE_APPROVED`). Omit reminder ids. Trace must show `[SLA Reminder Fired]` in duration order, then the completing event, never `[SLA Timeout Fired]`.
        - Overdue path: set `autoAdvanceTimers: true` and OMIT the completing event. Put `TimeoutEvent` on that step's `nextSteps`. Trace must show reminders then `[SLA Timeout Fired]` (`QUOTE_RESPONSE_OVERDUE`).
        - Pause path: call simulate with no events. Status is WaitingForHumanTask; `pendingHumanTask.reminders` lists the schedule. Nothing fires until you choose happy path or overdue path.
        - Timer steps and HumanTask SLA are different. `autoAdvanceTimers` elapses Timer steps AND unlocks SLA overdue. Completing-event reminder injection does not need the flag.
        - Preferred prompt: `test_sla_reminders_in_simulator`. Preferred resource: `flowos://guides/sla-reminder-simulation`.

        Bounded-autonomy task law (runtime AI work, not design-time MCP chat):
        - Dual-kernel still applies. The agent may only return a legal `nextSteps` event. FlowOS hosts wait → DecisionPacket → agent → AutoCommitPolicy → `publish_event` or park as a HumanTask Smart Action.
        - Do not assign `actor: Agent` to a Decision `Default` skip. Do not auto-commit TimeoutEvent. Do not call `publish_event` from free-form chat; use `run_agent_task` or let the entry hook run.
        - Inspect Agent Context without running the agent: `get_agent_context` (live instance) or `preview_agent_context` (draft/published class + stepId). The payload is one object: Prompt + Data + Tools + redacted Provider.
        - Tenant BYO model: `register_plugin_binding` with `bindingType: agent` (provider/model/endpoint/apiKey). Step `agentProvider` is the alias. The key never appears in Agent Context.
        - Tenant prompts: create/edit with `upsert_agent_prompt` (or `register_plugin_binding` `bindingType: prompt`). List with `list_agent_prompts`. Step `agentPrompt` is the alias. Dashboard: Agent Prompts tab.
        - Declarative tools: step `agentTools` lists resource plugins (`LookupRecord:<capability>`, `QueryRecords:`, `FetchDocument:`, `SearchKnowledge:`, `CheckPolicy:`) plus notify plugins and `capability:*` writes. FlowOS prefetches read tools into Agent Context. The model does not call HTTP or see URLs.
        - Preferred prompt: `design_agent_handled_step`. Preferred resource: `flowos://guides/bounded-autonomy-tasks`.

        OS claim law (read before calling FlowOS an operating system):
        - Load prompt `check_os_release_gate` or resource `flowos://guides/os-release-gate`.
        - If VERDICT is not GREEN, FlowOS is a dual-kernel process engine with an MCP control plane. Do not declare it a business automation OS.
        - Preferred prompt: `check_os_release_gate`. Preferred resource: `flowos://guides/os-release-gate`.
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
                name = "test_sla_reminders_in_simulator",
                description = "Why and how to prove HumanTask/Command SLA reminders and TimeoutEvent in simulate_workflowclass / simulate_context_binding without starting a live waiting instance.",
                arguments = new[]
                {
                    new { name = "stepId", description = "Waiting step with SLA (e.g., ApproveQuote, ExecuteRepair)", required = false }
                }
            },
            new
            {
                name = "design_agent_handled_step",
                description = "How to design a waiting HumanTask/Command that an agent may decide using layered guidelines, with auto-commit only when policy matches.",
                arguments = new[]
                {
                    new { name = "stepId", description = "Waiting step to assign (e.g., ApproveQuote, ExecuteRepair)", required = false }
                }
            },
            new
            {
                name = "check_os_release_gate",
                description = "OS-1 Honesty Gate: whether FlowOS may be called a business automation operating system, or only a dual-kernel process engine.",
                arguments = new[]
                {
                    new { name = "audience", description = "Who is asking (agent or human). Does not change the verdict.", required = false }
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
        var stepId = arguments?["stepId"]?.ToString() ?? "ApproveQuote";

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

            "test_sla_reminders_in_simulator" => new
            {
                description = "How to test SLA reminders and timeouts in the FlowOS simulator",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new
                        {
                            type = "text",
                            text = SlaReminderSimulationGuide.Replace("{STEP_ID}", stepId)
                        }
                    }
                }
            },

            "design_agent_handled_step" => new
            {
                description = "How to design a bounded-autonomy agent-handled waiting step",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new
                        {
                            type = "text",
                            text = BoundedAutonomyTasksGuide.Replace("{STEP_ID}", stepId)
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

            "check_os_release_gate" => new
            {
                description = "OS-1 Honesty Gate",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new
                        {
                            type = "text",
                            text = OsReleaseGateGuide
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
                uri = "flowos://guides/sla-reminder-simulation",
                name = "SLA Reminder and Timeout Simulation Guide",
                description = "Why a simulate-to-Paid run is not a wall clock, and how to prove reminders vs TimeoutEvent without a live instance.",
                mimeType = "text/markdown"
            },
            new
            {
                uri = "flowos://guides/bounded-autonomy-tasks",
                name = "Bounded-Autonomy AI Task Guide",
                description = "How to assign actor Agent/Either, layered DecisionPacket guidelines, and auto-commit policy without letting the model invent transitions.",
                mimeType = "text/markdown"
            },
            new
            {
                uri = "flowos://guides/os-release-gate",
                name = "OS-1 Honesty Gate",
                description = "Must-pass criteria for calling FlowOS a business automation operating system rather than a workflow engine. If VERDICT is not GREEN, do not use the OS claim.",
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

            "flowos://guides/sla-reminder-simulation" => new
            {
                contents = new[]
                {
                    new
                    {
                        uri,
                        mimeType = "text/markdown",
                        text = SlaReminderSimulationGuide.Replace("{STEP_ID}", "ApproveQuote")
                    }
                }
            },

            "flowos://guides/bounded-autonomy-tasks" => new
            {
                contents = new[]
                {
                    new
                    {
                        uri,
                        mimeType = "text/markdown",
                        text = BoundedAutonomyTasksGuide.Replace("{STEP_ID}", "ApproveQuote")
                    }
                }
            },

            "flowos://guides/os-release-gate" => new
            {
                contents = new[]
                {
                    new
                    {
                        uri,
                        mimeType = "text/markdown",
                        text = OsReleaseGateGuide
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

        ## Repeatable paths (retry-password)

        If Work can travel the same edge more than once (`EnterPassword --PASSWORD_FAIL--> EnterPassword`), that is a cycle. Infinite loops are forbidden.

        Declare the cap on the looping step:

        ```json
        "nextSteps": {
          "PASSWORD_OK": "Unlocked",
          "PASSWORD_FAIL": "EnterPassword"
        },
        "pathLimits": {
          "PASSWORD_FAIL": { "maxTravels": 3, "onExceeded": "LockedOut" }
        }
        ```

        Runtime counts `from|event|to`. Travel 1–3 stay on EnterPassword. Travel 4 routes to `LockedOut` (or fails closed if `onExceeded` is omitted). Cyclic edges without `pathLimits` still cap at 5. Align Law: add a state-machine overflow transition such as `Authenticating + LOCKED_OUT → Locked`.

        ## Preferred MCP design loop

        1. `describe_workflowclass_schema`
        2. `create_draft_workflowclass` — declare `events`, `stateMachine.transitions`, and `workflow.steps` together.
        3. `validate_draft_workflowclass` then `lint_draft_workflowclass`
        4. `simulate_workflowclass` with the **full** event list, including every state-machine trigger, even after Decision auto-routes. To prove SLA reminders/timeouts, read `flowos://guides/sla-reminder-simulation` (do not start a live instance).
        5. Tenant context (do not strip roles or publish a throwaway no-roles variant):
           - `create_context_binding` against the **draft** template id, with `inputMapping` for canonical fields
           - `simulate_context_binding` with `revision: "draft"`, a real business `initialPayload`, optional `roles` for the trace, and the same full event list. SLA overdue uses `autoAdvanceTimers: true` without the completing event — see `flowos://guides/sla-reminder-simulation`.
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
        - SLA reminder vs timeout in the simulator: `test_sla_reminders_in_simulator` / `flowos://guides/sla-reminder-simulation`
        - Agent-handled waiting steps: `design_agent_handled_step` / `flowos://guides/bounded-autonomy-tasks`
        """;

    public const string SlaReminderSimulationGuide =
        """
        # FlowOS SLA Reminder and Timeout Simulation Guide

        Target waiting step: {STEP_ID}

        Read this before you start a live workflow instance "to wait for reminders."

        ## Why agents get confused

        FlowOS accepts `sla.reminders` and `sla.timeoutEvent` on validate. A later `simulate_workflowclass` / `simulate_context_binding` with the **full business event list to Paid** still looks like a straight workflow executor:

        - It does **not** wait 2h / 12h / 24h of wall-clock time.
        - If the completing event is in `events` (`QUOTE_APPROVED`, `REPAIR_COMPLETED`), the human responded **in time**. Timeout must **not** fire. That is correct.
        - Reminders **do** fire on that same run: the simulator injects them in duration order **before** the completing event. Look for `[SLA Reminder Fired]` in `executionTrace` / `trace`.
        - `isValid: true` plus `finalState: Paid` does **not** by itself prove timeout. It only proves the happy path.

        Do not conclude "the simulator has no timer mode." Do not call `start_workflow` and leave the instance sitting at {STEP_ID}.

        Tools: `simulate_workflowclass` (blueprint/draft) and `simulate_context_binding` (bound payload). Same rules.

        ## Blueprint that can be tested

        Put SLA on a **waiting** HumanTask or Command (a step that actually sits until an event). Not on a Decision/`Default` Command that auto-routes away immediately.

        ```json
        {
          "stepId": "{STEP_ID}",
          "stepType": "HumanTask",
          "sla": {
            "duration": "24h",
            "timeoutEvent": "QUOTE_RESPONSE_OVERDUE",
            "reminders": [
              { "duration": "2h", "triggerEvent": "QUOTE_REMINDER_SENT" },
              { "duration": "12h", "triggerEvent": "QUOTE_REMINDER_SENT" }
            ]
          },
          "nextSteps": {
            "QUOTE_APPROVED": "MaterialDecision",
            "QUOTE_RESPONSE_OVERDUE": "QuoteOverdue"
          }
        }
        ```

        Required:
        1. Declare reminder and timeout ids in `events`.
        2. Put **TimeoutEvent on `nextSteps`** (and a state-machine transition if state should change, e.g. Assigned → Overdue).
        3. Reminder events do **not** need `nextSteps`. They may loop back, be state-only, or be notification-only. Simulation still logs `[SLA Reminder Fired]` and stays on the step.
        4. Duration: `"2h"` = 2 hours after step entry. `"-2h"` = 2 hours before timeout.
        5. Clock events are not human actions. Simulation does not require the HumanTask role on reminder/timeout.

        Same pattern for ExecuteRepair: timeout `8h`, reminders `2h`/`6h`, timeout event `REPAIR_OVERDUE`.

        ## Three simulator tests (use all three)

        ### 1. Pause — schedule is visible, nothing fires

        Call simulate with **no** `events` (or stop the list so the instance is sitting on {STEP_ID}).

        Expect:
        - `status`: `WaitingForHumanTask`
        - `pendingHumanTask.reminders` lists each `duration` + `triggerEvent`
        - no `[SLA Reminder Fired]`, no timeout

        This only proves the blueprint is waiting. It is not the reminder test.

        ### 2. Happy path — reminders then completing event, never timeout

        `events` must include the completing `nextSteps` key (`QUOTE_APPROVED`). **Omit** `QUOTE_REMINDER_SENT` and `QUOTE_RESPONSE_OVERDUE`. Do not set `autoAdvanceTimers`.

        Expect, in order:
        1. `[SLA Reminder Fired]` for 2h
        2. `[SLA Reminder Fired]` for 12h
        3. `QUOTE_APPROVED` advances the step
        4. **no** `[SLA Timeout Fired]` / `QUOTE_RESPONSE_OVERDUE`

        Meaning: the customer answered after the reminder schedule and before 24h. A run that reaches Paid without timeout is the intended happy path.

        ### 3. Overdue — reminders then TimeoutEvent

        Set `"autoAdvanceTimers": true`. **Omit** the completing event (`QUOTE_APPROVED` must not be in the remaining `events` while sitting on {STEP_ID}).

        Expect, in order:
        1. `[SLA Reminder Fired]` (2h, then 12h)
        2. `[SLA Timeout Fired]` with `QUOTE_RESPONSE_OVERDUE`
        3. step follows `nextSteps.QUOTE_RESPONSE_OVERDUE`

        This is the timer-specific simulation mode. You do not need a live instance.

        Example overdue call:

        ```json
        {
          "id": "<draft-or-class-uuid>",
          "autoAdvanceTimers": true,
          "events": ["JOB_REQUESTED"]
        }
        ```

        Stop the list so the current step is {STEP_ID}. Do not include `QUOTE_APPROVED`.

        Context-binding overdue:

        ```json
        {
          "contextType": "ServiceRepair",
          "revision": "draft",
          "autoAdvanceTimers": true,
          "events": [{ "eventType": "JOB_REQUESTED" }]
        }
        ```

        ## What each flag/event means

        | You send | Simulator does |
        |---|---|
        | Completing `nextSteps` event, no reminder ids | Inject reminders in duration order, then complete. No timeout. |
        | `autoAdvanceTimers: true`, no completing event | Inject reminders, then TimeoutEvent. |
        | No events | Pause; show `pendingHumanTask.reminders`. |
        | Reminder ids already in `events` | Use yours; do not double-inject that id. |
        | Completing event **and** `autoAdvanceTimers: true` | Still no timeout (human responded in time). Flag only elapses Timer **steps** plus overdue when no completing event remains. |

        Timer **steps** (`stepType: Timer`) are not HumanTask SLA. `autoAdvanceTimers` also auto-elapses those steps. Do not mix the two tests.

        ## Pass/fail checklist

        - Validate `isValid: true` — config only.
        - Pause run shows reminders — schedule only.
        - Happy-path trace contains `[SLA Reminder Fired]` and the completing event, not timeout — reminder proof.
        - Overdue trace contains `[SLA Timeout Fired]` / `QUOTE_RESPONSE_OVERDUE` — timeout proof.
        - If happy path has no `[SLA Reminder Fired]`: SLA is not on a waiting step, or the completing event is not that step's `nextSteps` key (Decision `Default` auto-route skipped the wait).
        - If overdue is `InvalidEvent` / Denied: TimeoutEvent is missing from `nextSteps`.
        - If overdue is `Denied` for roles: you used a human event, not the clock path; clock events do not need the task role.

        Live `start_workflow` is for production execution after publish/activate, not for proving the 2h reminder.
        """;

    public const string OsReleaseGateGuide =
        """
        # OS-1 Honesty Gate

        Canonical page: docs/19-os-release-gate.md

        ```
        GATE=OS-1 Honesty Gate
        VERDICT=GREEN
        CLAIM_ALLOWED=true
        OS-ID=done
        OS-KERNEL=done
        OS-LAW=done
        OS-INBOX=done
        OS-INT=done
        OS-AI=done
        OS-SIM=done
        OS-OPS=done
        OS-COMM=done
        ```

        GREEN claim (allowed only when VERDICT=GREEN):
        FlowOS is a business automation operating system: tenant work runs under dual-kernel Law (state machine) and Work (workflow), gated by capabilities and deny-only policy, with a HumanTask inbox, capability-bound integrations, a hosted DecisionPacket / autoCommit loop, side-effect-free simulation, and paid runtime entitlement.

        RED claim (required if VERDICT is not GREEN):
        FlowOS is a dual-kernel process engine with an MCP control plane — not yet a business automation operating system.

        If VERDICT is not GREEN, do not call FlowOS a business automation OS. Name kernels (dual-kernel, context bindings, MCP, DecisionPacket) without the OS sentence.

        ## Must-pass

        | ID | Pillar | Status | Proof | Engine blocker |
        |---|---|---|---|---|
        | OS-ID | Identity / tenancy | done | register-tenant, JWT login, TenantApiKey, credential tenant wins, AllowMockAuth Development-only | none for v1 |
        | OS-KERNEL | Dual-kernel | done | WorkflowEngine.Advance, class-backed fail-closed, inverted StateMachineGapTests | none for v1 (L-LAW-STATIC later) |
        | OS-LAW | Policy / capabilities | done | RequiresCapability, ApproveAsPublic admin-only, DefaultPolicyEvaluator malformed JSON fail-closed, MCP-APPROVAL-REQUIRED | none for v1 |
        | OS-INBOX | HumanTask inbox + SLA | done | GET /api/tasks role filter, complete_task, insights on task, SLA timeout not auto-committed | none for v1 (L-INBOX-UX later) |
        | OS-INT | Integrations | done | register_capability_binding, LookupRecord/QueryRecords/FetchDocument/SearchKnowledge/CheckPolicy | none for v1 |
        | OS-AI | DecisionPacket loop | done | run_agent_task, TenantLlmWorkflowAgent, flowos-risk, get_agent_context, upsert_agent_prompt, BoundedAutonomyTests | none for v1 |
        | OS-SIM | Simulation | done | simulate_workflowclass, simulate_context_binding | none for v1 |
        | OS-OPS | Operations | done | health, DLQ, replay_workflow_history, dual hosts | none for v1 (OTEL is later) |
        | OS-COMM | Entitlement | done | MCP-PLAN-REQUIRED, RequireRuntimePlan, EntitlementHttpTests | none for v1 (payment provider is later) |

        Later (does not block GREEN): L-PAY payment provider, L-SSO OIDC, L-USERS invite/SCIM, L-OTEL, L-LAW-STATIC publish-time Law projection, L-POLICY richer ConditionJson, L-INBOX-UX dashboard Inbox + list_tasks after role filtering exists.

        How the gate stays GREEN: keep every must-pass done here and in docs/19. Tests fail if GREEN while any must-pass is not done.
        """;

    public const string BoundedAutonomyTasksGuide =
        """
        # FlowOS Bounded-Autonomy AI Task Guide

        Target waiting step: {STEP_ID}

        Dual-kernel law still holds. The state machine decides what is legal. The workflow graph decides where the instance sits. An agent must not invent transitions.

        ## Suggest always; auto-commit only when policy matches

        1. Keep {STEP_ID} a **waiting** HumanTask or Command. Do **not** make it a Decision with `Default`/`true` so the AI "skips" the gate.
        2. Set `actor` to `Agent` or `Either` (`Human` is the default and never auto-commits).
        3. Create/edit the **prompt** with `upsert_agent_prompt` (title/system/instructions) and point the step with `agentPrompt`. Optional template fallback: `decisionGuideline`. Inspect the composed context with `preview_agent_context` before go-live, and `get_agent_context` on a live instance.
        4. Put **the case + tenant policy** on the context binding: `inputMapping` / canonical fields plus optional `policyGuideline`.
        5. Declare `autoCommit.minConfidence` and `autoCommit.allowedEvents` as a **subset of `nextSteps`**. Those events must also exist on the state machine.
        6. Never put `TimeoutEvent` or SLA reminder events in `autoCommit.allowedEvents`. Overdue stays timer-owned.
        7. Keep `requiredRoles` as the human fallback. If policy fails, FlowOS parks a HumanTask Smart Action from the insight.

        FlowOS hosts the loop: wait → DecisionPacket → `IWorkflowAgent` → AutoCommitPolicy → `PublishEventCommand` (actor `Agent:{id}`) or park. Do **not** teach an external chat agent to `publish_event` from free text. Call `get_agent_context` or `preview_agent_context` to inspect Prompt/Data/Tools/Provider; call `suggest_agent_action` to run the agent without publishing; call `run_agent_task` only to request the hosted loop.

        ## DecisionPacket / Agent Context (prompt, data, tools, provider)

        - **Prompt**: tenant prompt binding (`agentPrompt` → title/system/instructions) plus template `decisionGuideline`, binding `policyGuideline`, and objective. Create/edit the prompt independently; do not bake the whole prompt into the workflow JSON.
        - **Data**: canonical case fields, event payloads, SLA reminder/timeout facts, plus prefetched `ToolResults` from tenant resource plugins
        - **Tools**: legal `nextSteps` events (always) plus declared `agentTools`. Resource plugins (`LookupRecord`, `QueryRecords`, `FetchDocument`, `SearchKnowledge`, `CheckPolicy`) prefetch through tenant capability bindings. Notify/write plugins are listed but not prefetched. The model never sees URLs or calls HTTP.
        - **Provider**: tenant-owned. Register with `register_plugin_binding` (`bindingType: agent`, `sourceName` = step `agentProvider`, `providerName` openai/anthropic/azure-openai/google/custom/flowos-risk, `configuration` `{model,endpoint,apiKey}`). List/resolve return `hasApiKey`, never the secret. Omit `apiKey` on update to keep the stored key. The key is never placed in Agent Context.

        The model may only return an event from legal `nextSteps`. Illegal suggestions are dropped. Until an LLM HTTP client is wired, FlowOS still hosts `RiskAnalysisAgent` as the fixture runtime; the tenant provider settings are stored and redacted on the packet.

        ## Example

        First create/edit the tenant prompt with `upsert_agent_prompt`:

        ```json
        {
          "alias": "quote-approval",
          "kind": "markdown",
          "title": "Quote approval",
          "system": "You are a service advisor assistant. Only suggest legal nextSteps events.",
          "instructions": "ApproveQuote: accept if quote is within 15% of estimate; else request revision; never approve missing labor hours."
        }
        ```

        First register the tenant model (write-only key):

        ```json
        {
          "bindingType": "agent",
          "sourceName": "quote-llm",
          "providerName": "openai",
          "configuration": { "model": "gpt-4o-mini", "apiKey": "<tenant-key>" }
        }
        ```

        ```json
        {
          "stepId": "{STEP_ID}",
          "stepType": "HumanTask",
          "actor": "Either",
          "requiredRoles": ["ServiceAdvisor"],
          "decisionGuideline": "ApproveQuote: accept if quote is within 15% of estimate; else request revision; never approve missing labor hours.",
          "agentPrompt": "quote-approval",
          "agentProvider": "quote-llm",
          "agentTools": [
            "LookupRecord:crm.customer.get.v1",
            "CheckPolicy:policy.approval-limit.v1",
            "FetchDocument:docs.quote.get.v1",
            "SearchKnowledge:kb.policy.search.v1",
            "QueryRecords:inventory.parts.query.v1"
          ],
          "autoCommit": {
            "minConfidence": 0.9,
            "allowedEvents": ["QUOTE_APPROVED"]
          },
          "nextSteps": {
            "QUOTE_APPROVED": "MaterialDecision",
            "QUOTE_REVISION_REQUESTED": "ReviseQuote",
            "QUOTE_RESPONSE_OVERDUE": "QuoteOverdue"
          }
        }
        ```

        Binding overlay (do not copy the whole template guideline):

        ```json
        {
          "entityType": "ServiceRepair",
          "policyGuideline": "ServiceRepair quotes above the shop's approvalLimit must request revision even if the template would accept.",
          "inputMapping": { "Amount": "quote.total", "ApprovalLimit": "shop.approvalLimit" }
        }
        ```

        ## What not to do

        - Do not use DecisionProvider plugins as LLM judgment. They stay deterministic expression routers.
        - Do not put the API key on the step, in `decisionGuideline`, or in MCP chat. It lives only on the `agent` plugin binding.
        - Do not let the model invent URLs or free-form HTTP. Declare tools from existing plugins/capabilities.
        - Do not auto-commit `QUOTE_VOIDED` unless it is explicitly in `allowedEvents`.
        - Do not start a live instance just to "let the AI think." Design-time still uses simulate tools.
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
