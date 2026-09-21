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
        3. Policy Governance (capabilities as execution gate; roles as inbox / grant bags). Tenant IAM roles are not workflow inbox roles.

        Canonical 5-Step Operating Lifecycle for AI Agents:
        ---------------------------------------------------
        Follow this exact sequence to operate any business workflow in FlowOS:

        [Step 1: Inspect Schema & Draft Blueprint]
          • Call `describe_workflowclass_schema` to inspect the canonical JSON blueprint structure.
          • Call `create_draft_workflowclass` with `name`, `version`, and `blueprint`.
            The blueprint MUST define:
            - `stateMachine`: `initialState`, `states`, and `transitions` (fromState, toState, triggerEvent).
            - `workflow`: `startStepId` and `steps` (stepId, stepType, requiredRoles, requiredCapabilities, nextSteps). Capability is the execution gate; requiredRoles is inbox only.
            - `events`: Event identifiers with `requiredCapabilities` (gate, typically event.publish.<eventId>) and optional `allowedRoles` (inbox hint). Empty allowedRoles must not hide step requiredRoles.
            - `roles`: Business-context grant bags (`name` + `grantedCapabilities`). Inbox label only; never written to tenant IAM Role tables.
            - `capabilities`: Catalog of codes referenced by events and HumanTasks.

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
            WorkflowClass roles are business-context declarations, not FlowOS tenant/IAM roles; activation never writes Roles or TenantUserRole.
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
          • When the waiting step is actor `Agent`/`Either`, do not invent the next event in chat. Inspect with `get_agent_context`, then call `run_agent_task` so FlowOS hosts DecisionPacket → tenant LLM (or `flowos-risk`) → AutoCommitPolicy.
          • Call `suggest_agent_action` to run the same agent without publishing (advisory only).
          • Call `get_workflow_instance_status` or `list_workflow_instances` to inspect runtime status, current step, and execution history.

        Tip: Call MCP Prompts (`prompts/list` & `prompts/get`) or read MCP Resources (`resources/list` & `resources/read`) for full templates.
          Preferred prompt: `design_dual_kernel_workflow`. Preferred resource: `flowos://guides/dual-kernel-design`.
          For SLA reminders/timeouts: prompt `test_sla_reminders_in_simulator` and resource `flowos://guides/sla-reminder-simulation`.
          For AI task automation: prompt `automate_waiting_task_with_ai_agent` and resource `flowos://guides/ai-task-automation`.

        Dual-kernel design law (read before drafting Decision steps):
        - The workflow graph moves `currentStep`. The state machine moves `currentState`. They are independent kernels.
        - A Decision with `conditions.Default` or `conditions.true` auto-routes the workflow WITHOUT consuming a business event.
        - If the state machine still requires that event (e.g. ApproveQuote auto-routes to MaterialDecision while Assigned → Quoted needs QUOTE_APPROVED), you MUST still send the event in `simulate_workflowclass` / `simulate_context_binding` / `publish_event`.
        - FlowOS then applies it as a state-only catch-up: step stays put, state advances. Omitting it leaves state behind; the next event is Denied as a state-machine violation.
        - Preferred design: HumanTask/Command `nextSteps` consume the same event the state machine uses. Do not auto-skip a legal gate unless you still emit that event.
        - Repeatable paths (retry-password, resubmit, pin re-entry) MUST declare `pathLimits` on the looping nextSteps key: `{ "maxTravels": 3, "onExceeded": "LockedOut" }`. The engine counts each travel and fails closed or routes to onExceeded. Cyclic edges without a declaration still cap at 5.
        - WorkflowClass `roles[]`/`capabilities[]` are business-context vocabulary compiled onto `WorkflowDefinition.BusinessRoles`. They are never written to FlowOS tenant Role/TenantUserRole tables. Capability is the execution gate; `requiredRoles` is inbox only. A Director granted Manager event capabilities can fire those events without being the inbox role. Payload-decided inbox (Amount > 5000 → Director, else Manager) uses a Work Decision plus dual Law transitions on the same EventId; `simulate_workflowclass` picks the first eligible guard. `simulate_context_binding` may use a Draft template. Do not publish a stripped-roles copy just to simulate. `validate_context_binding` / `activate_context_binding` still need a Published source. `CTX-ROLE-002` only means a role override mapped to an empty name.
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

        Bounded-autonomy / AI task-automation law (runtime AI work, not design-time MCP chat):
        - Dual-kernel still applies. The agent may only return a legal `nextSteps` event. FlowOS hosts wait → DecisionPacket → tenant LLM (`TenantLlmWorkflowAgent`) or `flowos-risk` → AutoCommitPolicy → `publish_event` (actor `Agent:{id}`) or park as a HumanTask Smart Action.
        - Do not assign `actor: Agent` to a Decision `Default` skip. Do not auto-commit TimeoutEvent. Do not call `publish_event` from free-form chat; use `run_agent_task` or let the entry hook run.
        - Register the tenant model with `upsert_agent_provider` (alias = step `agentProvider`, write-only `apiKey`). Create/edit the prompt with `upsert_agent_prompt` (alias = step `agentPrompt`). Dashboard: Application → select workflow → AI Context → Prompts / Providers. Never paste the key into MCP chat or the blueprint.
        - Inspect Agent Context without running the agent: `get_agent_context` (live instance) or `preview_agent_context` (draft/published class + stepId). The payload is one object: Prompt + Data + Tools + redacted Provider (`hasApiKey`, never the secret).
        - Live automation: publish the human gate that reaches the Agent step (e.g. `EVT-SUBMIT` on QuoteAutoReview), then `run_agent_task`. Paid tenants default to FlowOS hosted OpenAI (`flowos-hosted`, platform key + daily quota). BYO `openai`/`anthropic` still wins when the step alias is a tenant binding with a key. Omit `agentId` unless you want `flowos-risk`. `suggest_agent_action` is the same call without publishing.
        - Hybrid execution: API workflow entry enqueues a PostgreSQL-backed job for the API worker; MCP `run_agent_task` / `suggest_agent_action` uses the same durable job and lease path but waits synchronously for a terminal result. A timed-out MCP wait does not erase the queued job.
        - Save `jobId` and `executionId` from run responses. Query `get_agent_execution_history` for sanitized per-run status and `get_agent_evaluation_metrics` for a bounded UTC window. Agent commits are attributed as `Agent:{id}`; a later human event remains attributable to that human and may be evaluated as an override.
        - Hosted failures: `MCP-PLAN-REQUIRED` means entitlement, `MCP-HOSTED-LLM-QUOTA` is the persistent per-tenant UTC-day cap, and `MCP-HOSTED-LLM-UNAVAILABLE` is host configuration. Persisted execution codes are sanitized (`ENTITLEMENT_DENIED`, `HOSTED_QUOTA_DENIED`, `PROVIDER_CONFIGURATION`, or provider taxonomy codes).
        - Simulate Agent progress without a live LLM: `simulate_workflowclass` and `simulate_context_binding` use the shared deterministic evaluator when a waiting HumanTask/Command is actor Agent/Either and no completing event is queued. Default suggestion is the first `autoCommit.allowedEvents` at confidence 1.0. Override with `simulatedAgent` `{event,confidence,agentId}`. Set `autoAdvanceAgents: false` to wait and inspect `pendingAgentTask`. Explicit events still win. Timeout stays timer-owned.
        - Declarative tools: step `agentTools` lists resource plugins (`LookupRecord:<connector>`, `QueryRecords:`, `FetchDocument:`, `SearchKnowledge:`, `CheckPolicy:`) plus notify plugins and `connector:*` writes (legacy `capability:*`). FlowOS prefetches read tools into Agent Context. The model does not call HTTP or see URLs.
        - Observability is evaluation, not self-learning. FlowOS never retrains a model or silently mutates prompts, auto-commit policy, workflow events, or execution records from these metrics. Outcomes without immutable evidence remain explicitly unevaluated.
        - Preferred prompts: `automate_waiting_task_with_ai_agent` (live loop) and `design_agent_handled_step` (step JSON). Preferred resources: `flowos://guides/ai-task-automation` and `flowos://guides/bounded-autonomy-tasks`.

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
                    new { name = "stepId", description = "Waiting step to assign (e.g., ApproveQuote, AgentReview, ExecuteRepair)", required = false }
                }
            },
            new
            {
                name = "automate_waiting_task_with_ai_agent",
                description = "How to bind a tenant AI Agent (prompt + provider) and run it on a waiting Agent/Either task: preview_agent_context, start_workflow, publish the human gate, run_agent_task. Simulate_* does not call the live model.",
                arguments = new[]
                {
                    new { name = "workflowName", description = "Sample or tenant workflow (default QuoteAutoReview)", required = false },
                    new { name = "stepId", description = "Waiting Agent/Either step (default AgentReview)", required = false }
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
        var automationWorkflow = arguments?["workflowName"]?.ToString() ?? "QuoteAutoReview";
        var automationStep = arguments?["stepId"]?.ToString() ?? "AgentReview";

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

            "automate_waiting_task_with_ai_agent" => new
            {
                description = "How to run a tenant AI Agent on a waiting workflow task",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new
                        {
                            type = "text",
                            text = AiTaskAutomationGuide
                                .Replace("{WORKFLOW_NAME}", automationWorkflow)
                                .Replace("{STEP_ID}", automationStep)
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
                                4. Complete human tasks with `complete_task`. If the waiting step is actor Agent/Either, call `get_agent_context` then `run_agent_task` instead of inventing the event in chat.
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
                                   - Does the caller have a granted capability for this event (`event.publish.<event>`)? Inbox `requiredRoles` only decide who sees the HumanTask.
                                   - Did payload conditions route the inbox to a different role (e.g. Amount > 5000 → Director)?
                                   - If currentStep is ahead of currentState, a Decision auto-route skipped a gate: publish the unused state-machine event (state-only catch-up). Read `flowos://guides/dual-kernel-design`.
                                4. If the waiting step is actor Agent/Either, call `get_agent_context` then `run_agent_task` (or `suggest_agent_action` to inspect without publishing). Otherwise call `suggest_agent_action` with the fixture `RiskAnalysisAgent` only when no tenant `agentProvider` is bound.
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
                uri = "flowos://guides/ai-task-automation",
                name = "AI Agent Task Automation Guide",
                description = "How to register a tenant prompt and LLM provider, preview Agent Context, start a live instance, and run_agent_task on a waiting Agent/Either step. simulate_* does not call the live model.",
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

            "flowos://guides/ai-task-automation" => new
            {
                contents = new[]
                {
                    new
                    {
                        uri,
                        mimeType = "text/markdown",
                        text = AiTaskAutomationGuide
                            .Replace("{WORKFLOW_NAME}", "QuoteAutoReview")
                            .Replace("{STEP_ID}", "AgentReview")
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

        ## Role and capability (inbox vs gate)

        Capability is the execution gate. `requiredRoles` / event `allowedRoles` are the HumanTask inbox only.

        - Human events and HumanTasks declare `requiredCapabilities` (`event.publish.<eventId>`). GOV-002 if missing.
        - `roles[].grantedCapabilities` is the grant bag. Director may hold Manager event grants without being the Manager inbox.
        - Empty event `allowedRoles: []` must not hide step `requiredRoles`.
        - `simulate_workflowclass` authorizes the **event**, not the inbox label. Command `Default`/`true` auto-routes stay authorized.
        - Payload-decided inbox: dual Law transitions on the same EventId with different `condition` (Amount > 5000 → PendingDirector, else PendingManager) plus a Work Decision. Simulate with the matching role for the landing inbox, or a role that holds that event's capability.
        - WorkflowClass roles are never FlowOS tenant IAM roles. `CTX-ROLE-002` is an empty override name, not a missing tenant Role row.

        ## Preferred MCP design loop

        1. `describe_workflowclass_schema`
        2. `create_draft_workflowclass` — declare `events`, `stateMachine.transitions`, and `workflow.steps` together.
        3. `validate_draft_workflowclass` then `lint_draft_workflowclass`
        4. `simulate_workflowclass` with the **full** event list, including every state-machine trigger, even after Decision auto-routes. To prove SLA reminders/timeouts, read `flowos://guides/sla-reminder-simulation` (do not start a live instance).
        5. Tenant context (do not strip roles or publish a throwaway no-roles variant):
           - `create_context_binding` against the **draft** template id, with `inputMapping` for canonical fields
           - `simulate_context_binding` with `revision: "draft"`, a real business `initialPayload`, optional `roles` for the trace, and the same full event list. SLA overdue uses `autoAdvanceTimers: true` without the completing event — see `flowos://guides/sla-reminder-simulation`.
           - `CTX-ROLE-002` means a role override mapped to an empty name. Business-context roles do not need a matching FlowOS tenant role.
        6. `publish_workflowclass` with `confirmHumanApproval: true` when required, then `validate_context_binding`
        7. `activate_context_binding` only after draft simulation is Allowed through the expected final state
        8. Runtime: `start_workflow` then `publish_event` for each remaining state-machine trigger

        ## Context-binding rules

        - Bindings never create roles or permissions.
        - Role overrides that rename an undeclared template role fail with `CTX-ROLE-001`. Empty override names fail with `CTX-ROLE-002`. A FlowOS tenant role does not have to exist for a business-context role name.
        - Do not publish a stripped-roles copy of the template just to bind and simulate. Bind the draft, simulate, then publish once.
        - `simulate_context_binding` never persists instances or snapshots. A Denied trace is a design signal, not a reason to delete the template.

        ## Tool names to use

        - Design sandbox: `simulate_workflowclass`
        - Bound business payload: `simulate_context_binding`
        - After a live instance exists: `fork_workflow_simulation` / `replay_workflow_history`
        - SLA reminder vs timeout in the simulator: `test_sla_reminders_in_simulator` / `flowos://guides/sla-reminder-simulation`
        - Agent-handled waiting steps: `design_agent_handled_step` / `flowos://guides/bounded-autonomy-tasks`
        - Live AI task automation: `automate_waiting_task_with_ai_agent` / `flowos://guides/ai-task-automation` (`upsert_agent_provider` + `run_agent_task`)
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
          "requiredRoles": ["ServiceAdvisor"],
          "requiredCapabilities": ["event.publish.QUOTE_APPROVED"],
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
        | OS-INT | Integrations | done | register_connector, LookupRecord/QueryRecords/FetchDocument/SearchKnowledge/CheckPolicy | none for v1 |
        | OS-AI | DecisionPacket loop | done | run_agent_task, TenantLlmWorkflowAgent, flowos-hosted OpenAI, upsert_agent_provider, flowos-risk, get_agent_context, upsert_agent_prompt, BoundedAutonomyTests | none for v1 |
        | OS-SIM | Simulation | done | simulate_workflowclass, simulate_context_binding | none for v1 |
        | OS-OPS | Operations | done | health, DLQ, replay_workflow_history, dual hosts | none for v1 (OTEL is later) |
        | OS-COMM | Entitlement | done | MCP-PLAN-REQUIRED, RequireRuntimePlan, EntitlementHttpTests | none for v1 (payment provider is later) |

        Later (does not block GREEN): L-PAY payment provider, L-SSO OIDC, L-USERS invite/SCIM, L-OTEL, L-LAW-STATIC publish-time Law projection, L-POLICY richer ConditionJson, L-INBOX-UX dashboard Inbox + list_tasks after role filtering exists.

        How the gate stays GREEN: keep every must-pass done here and in docs/19. Tests fail if GREEN while any must-pass is not done.
        """;

    public const string AiTaskAutomationGuide =
        """
        # FlowOS AI Agent Task Automation

        Target workflow: {WORKFLOW_NAME}
        Target waiting step: {STEP_ID}

        Use this when a HumanTask/Command must be decided by a **real tenant AI Agent** (AI Context prompt + provider), not by the simulator's AutoCommitEvaluator.

        `simulate_workflowclass` / `simulate_context_binding` never call the tenant model. They only prove graph, Law, inbox, and auto-commit policy. Live automation uses a **runtime instance**.

        ## Register AI Context (once per tenant)

        1. Prompt — `upsert_agent_prompt`. Point the step with `agentPrompt`.
        2. Provider — `upsert_agent_provider` (preferred) or `register_plugin_binding` `bindingType: agent`. Point the step with `agentProvider`. Write-only `apiKey`. List/get return `hasApiKey`, never the secret. Omit `apiKey` on later updates to keep the stored key.
        3. Dashboard equivalent: tenant portal → **Application** → select the workflow → scroll to **AI Context** → **Prompts** / **Providers**. Do not use the top **Keys** tab (that is the FlowOS API key).
        4. Do **not** paste the LLM key into MCP chat, the blueprint, `decisionGuideline`, or Agent Context.

        Example prompt:

        ```json
        {
          "alias": "quote-approval",
          "title": "Quote approval",
          "system": "You are a FlowOS workflow agent. Suggest one legal nextSteps event as JSON.",
          "instructions": "Accept if Amount is at or below ApprovalLimit and within 15% of Estimate. Otherwise do not auto-accept."
        }
        ```

        Example provider (key stays on the tenant store):

        ```json
        {
          "alias": "quote-llm",
          "providerName": "openai",
          "model": "gpt-4o-mini",
          "apiKey": "<tenant-key>"
        }
        ```

        Paid default is `flowos-hosted` (FlowOS OpenAI). Set `FLOWOS_HOSTED_LLM_API_KEY` on the host. `flowos-risk` needs no key and hosts `RiskAnalysisAgent`; it does **not** use the tenant prompt as an HTTP model.

        ## Design the waiting step

        Keep {STEP_ID} a waiting HumanTask or Command. Set `actor` to `Agent` or `Either`. Set `agentPrompt` and `agentProvider` to the aliases above. Declare `autoCommit.allowedEvents` as a subset of `nextSteps`. Never put TimeoutEvent or SLA reminders in `allowedEvents`.

        Seeded sample: **QuoteAutoReview** / step **AgentReview** (`agentPrompt: quote-approval`, `agentProvider: flowos-hosted`). Amount ≤ 1500 routes to the agent; Amount > 1500 routes to Advisor (human). Paid plan + `FLOWOS_HOSTED_LLM_API_KEY` is enough — no tenant OpenAI key.

        ## Preview (does not run the model)

        ```json
        {
          "workflowClassId": "<class-id>",
          "stepId": "{STEP_ID}",
          "contextBindingId": "<quote-binding-id>",
          "canonicalContext": { "Amount": 900, "Estimate": 880 }
        }
        ```

        Call `preview_agent_context`. Confirm Prompt (system/instructions + policyGuideline), Data, Tools, and Provider (`hasApiKey: true`, no `apiKey` field).

        ## Live loop (this is the real agent)

        Runtime tools need a Managed/Enterprise plan (`MCP-PLAN-REQUIRED` on Trial).

        1. `start_workflow` with `workflowName: "{WORKFLOW_NAME}"` or the Quote `contextBindingId`.
        2. `publish_event` the **human** gate that reaches {STEP_ID} (QuoteAutoReview: `EVT-SUBMIT` with Amount ≤ 1500). The Agent event (`EVT-ACCEPT`) is **not** this call.
        3. `get_workflow_instance_status` — current step should be {STEP_ID}, actor Agent/Either.
        4. `get_agent_context` — inspect the live packet. Still does not run the model.
        5. `run_agent_task` with `{ "workflowInstanceId": "<id>" }`. Omit `agentId` so the factory follows step `agentProvider`. FlowOS calls the tenant LLM, restricts to legal `nextSteps`, then auto-commits or parks.
        6. `suggest_agent_action` is the same hosted call **without** publishing (advisory).
        7. If parked (`parkReason`), a human uses `complete_task` / `publish_event` on a legal nextSteps event. Either steps allow that override.

        The start/publish entry hook enqueues a PostgreSQL job for the API worker. `run_agent_task` is the explicit MCP path: it uses the same durable queue and distributed lease, then waits synchronously. If that wait times out, the job can still complete in the worker. Do not submit a different event merely because the MCP call timed out.

        Every run response includes `jobId` and `executionId`. Keep both:
        - `jobId` identifies queue/retry ownership.
        - `executionId` identifies one provider attempt and its sanitized telemetry.
        - Auto-committed events carry actor `Agent:{id}`. Human follow-up events retain human provenance.
        - `get_agent_execution_history` reads per-run status without prompt bodies, response bodies, API keys, or raw failures.
        - `get_agent_evaluation_metrics` reports bounded-window latency/tokens/quota/outcome calibration. Unevaluated means no immutable workflow outcome can yet be derived; it is not a negative label.

        ## What success looks like

        - Auto-commit: `autoCommitted: true`, actor `Agent:{id}`, current step leaves {STEP_ID}.
        - Park: `autoCommitted: false` plus `parkReason` (low confidence, illegal event, missing key, or event not in `allowedEvents`).
        - Missing key: `TenantLlmWorkflowAgent` fails with provider missing an API key — register it on the tenant, do not put it in chat.

        ## Failure troubleshooting

        - `MCP-PLAN-REQUIRED` / persisted `ENTITLEMENT_DENIED`: activate Managed/Enterprise; no provider call was made.
        - `MCP-HOSTED-LLM-QUOTA` / persisted `HOSTED_QUOTA_DENIED`: the durable UTC-day tenant cap is exhausted. Wait for UTC reset, raise the operator cap, or bind BYO. BYO usage is not charged to hosted quota.
        - `MCP-HOSTED-LLM-UNAVAILABLE` / persisted `PROVIDER_CONFIGURATION`: configure the platform key on the process that executes the job.
        - `PROVIDER_AUTH`, `PROVIDER_RATE_LIMIT`, `PROVIDER_TIMEOUT`, `PROVIDER_UNAVAILABLE`: inspect provider/BYO configuration and retry policy. Failed provider runs never generate a successful AgentInsight.
        - `simulate_*` remains deterministic and never calls or verifies a paid provider.

        History and metrics are read-only evaluation. FlowOS does not train itself or automatically rewrite prompts, workflow policy, auto-commit thresholds, execution rows, or immutable workflow events.

        ## What not to do

        - Do not ask `simulate_workflowclass` to call OpenAI. Use `autoAdvanceAgents` / `simulatedAgent` only to prove policy.
        - Do not `publish_event` `EVT-ACCEPT` from free-form chat to "be the agent."
        - Do not lock `agentId` to `RiskAnalysisAgent` when the step uses `flowos-hosted` or a BYO `agentProvider`.
        - Do not put the API key on the step or in Agent Context.
        """;

    public const string BoundedAutonomyTasksGuide =
        """
        # FlowOS Bounded-Autonomy AI Task Guide

        Target waiting step: {STEP_ID}

        Dual-kernel law still holds. The state machine decides what is legal. The workflow graph decides where the instance sits. An agent must not invent transitions.

        ## Suggest always; auto-commit only when policy matches

        1. Keep {STEP_ID} a **waiting** HumanTask or Command. Do **not** make it a Decision with `Default`/`true` so the AI "skips" the gate.
        2. Set `actor` to `Agent` or `Either` (`Human` is the default and never auto-commits).
        3. Create/edit the **prompt** with `upsert_agent_prompt` (title/system/instructions) and point the step with `agentPrompt`. Register the **provider** with `upsert_agent_provider` (or dashboard Application → AI Context → Providers) and point the step with `agentProvider`. Optional template fallback: `decisionGuideline`. Inspect the composed context with `preview_agent_context` before go-live, and `get_agent_context` on a live instance.
        4. Put **the case + tenant policy** on the context binding: `inputMapping` / canonical fields plus optional `policyGuideline`.
        5. Declare `autoCommit.minConfidence` and `autoCommit.allowedEvents` as a **subset of `nextSteps`**. Those events must also exist on the state machine.
        6. Never put `TimeoutEvent` or SLA reminder events in `autoCommit.allowedEvents`. Overdue stays timer-owned.
        7. Keep `requiredRoles` as the HumanTask inbox and `requiredCapabilities` as the execution gate. If policy fails, FlowOS parks a HumanTask Smart Action from the insight.

        FlowOS hosts the loop: wait → DecisionPacket → `TenantLlmWorkflowAgent` (`flowos-hosted` platform OpenAI, or BYO step `agentProvider`) or `RiskAnalysisAgent` (`flowos-risk`) → AutoCommitPolicy → `PublishEventCommand` (actor `Agent:{id}`) or park. Do **not** teach an external chat agent to `publish_event` from free text. Call `get_agent_context` or `preview_agent_context` to inspect Prompt/Data/Tools/Provider; call `suggest_agent_action` to run the agent without publishing; call `run_agent_task` only to request the hosted loop. Paid hosted OpenAI uses `FLOWOS_HOSTED_LLM_API_KEY` on the host. `simulate_workflowclass` never calls that model.

        ## Simulate automated Agent progress (no live LLM)

        `simulate_workflowclass` and `simulate_context_binding` do not call a tenant model. When the current waiting HumanTask/Command is actor `Agent`/`Either` and no completing event is queued, the simulator runs the shared deterministic agent policy:

        | Input | Result |
        | --- | --- |
        | actor Agent/Either, `autoAdvanceAgents` default true, no `simulatedAgent` | First `autoCommit.allowedEvents` at confidence 1.0. Trace `[Agent Auto-Commit]`. |
        | `simulatedAgent: { event, confidence }` | That suggestion through the same policy. Low confidence or illegal event parks (`pendingAgentTask`). |
        | `autoAdvanceAgents: false` and no `simulatedAgent` | Wait. Inspect inbox / DecisionPacket fields. |
        | Completing `events` already queued | Explicit event wins (human/Either override). |
        | `autoAdvanceTimers: true`, no completing event, agent parked or disabled | Reminders then TimeoutEvent. Never auto-commit timeout. |

        ## DecisionPacket / Agent Context (prompt, data, tools, provider)

        - **Prompt**: tenant prompt binding (`agentPrompt` → title/system/instructions) plus template `decisionGuideline`, binding `policyGuideline`, and objective. Create/edit the prompt independently; do not bake the whole prompt into the workflow JSON.
        - **Data**: canonical case fields, event payloads, SLA reminder/timeout facts, plus prefetched `ToolResults` from tenant resource plugins
        - **Tools**: legal `nextSteps` events (always) plus declared `agentTools`. Resource plugins (`LookupRecord`, `QueryRecords`, `FetchDocument`, `SearchKnowledge`, `CheckPolicy`) prefetch through tenant capability bindings. Notify/write plugins are listed but not prefetched. The model never sees URLs or calls HTTP.
        - **Provider**: tenant-owned. Register with `upsert_agent_provider` (alias = step `agentProvider`, `providerName` openai/anthropic/azure-openai/google/custom/flowos-risk, `{model,endpoint,apiKey}`). `register_plugin_binding` `bindingType: agent` is the same store. List/get return `hasApiKey`, never the secret. Omit `apiKey` on update to keep the stored key. Dashboard: Application → select workflow → AI Context → Providers. The key is never placed in Agent Context.

        The model may only return an event from legal `nextSteps`. Illegal suggestions are dropped. When `agentProvider` resolves to openai/anthropic/azure-openai/google/custom **and** the tenant binding has a key, FlowOS hosts `TenantLlmWorkflowAgent`. Missing provider or `flowos-risk` uses `RiskAnalysisAgent` (no key).

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

        First register the tenant model with `upsert_agent_provider` (write-only key — do not paste it into this chat):

        ```json
        {
          "alias": "quote-llm",
          "providerName": "openai",
          "model": "gpt-4o-mini",
          "apiKey": "<tenant-key>"
        }
        ```

        ```json
        {
          "stepId": "{STEP_ID}",
          "stepType": "HumanTask",
          "actor": "Either",
          "requiredRoles": ["ServiceAdvisor"],
          "requiredCapabilities": ["event.publish.QUOTE_APPROVED"],
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
            { "eventId": "EVT-SUBMIT", "name": "Submit Expense", "category": "Human", "requiredCapabilities": ["event.publish.EVT-SUBMIT"], "allowedRoles": ["Employee"] },
            { "eventId": "EVT-APPROVE-MANAGER", "name": "Manager Approval", "category": "Human", "requiredCapabilities": ["event.publish.EVT-APPROVE-MANAGER"], "allowedRoles": ["Manager"] },
            { "eventId": "EVT-REJECT", "name": "Reject Claim", "category": "Human", "requiredCapabilities": ["event.publish.EVT-REJECT"], "allowedRoles": ["Manager"] }
          ],
          "stateMachine": {
            "initialState": "Draft",
            "states": ["Draft", "Submitted", "Approved", "Rejected"],
            "transitions": [
              { "fromState": "Draft", "toState": "Submitted", "eventId": "EVT-SUBMIT" },
              { "fromState": "Submitted", "toState": "Approved", "eventId": "EVT-APPROVE-MANAGER" },
              { "fromState": "Submitted", "toState": "Rejected", "eventId": "EVT-REJECT" }
            ]
          },
          "workflow": {
            "startStepId": "DraftStep",
            "steps": [
              {
                "stepId": "DraftStep",
                "stepType": "HumanTask",
                "requiredRoles": ["Employee"],
                "requiredCapabilities": ["event.publish.EVT-SUBMIT"],
                "nextSteps": { "EVT-SUBMIT": "ManagerReviewStep" }
              },
              {
                "stepId": "ManagerReviewStep",
                "stepType": "HumanTask",
                "requiredRoles": ["Manager"],
                "requiredCapabilities": ["event.publish.EVT-APPROVE-MANAGER", "event.publish.EVT-REJECT"],
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
            { "name": "Employee", "description": "Submits expense claims", "grantedCapabilities": ["event.publish.EVT-SUBMIT"] },
            { "name": "Manager", "description": "Reviews and approves claims", "grantedCapabilities": ["event.publish.EVT-APPROVE-MANAGER", "event.publish.EVT-REJECT"] },
            { "name": "Director", "description": "Can execute Manager-gated events without being the inbox role", "grantedCapabilities": ["event.publish.EVT-APPROVE-MANAGER", "event.publish.EVT-REJECT"] }
          ],
          "capabilities": [
            { "code": "event.publish.EVT-SUBMIT", "description": "Publish the submit event" },
            { "code": "event.publish.EVT-APPROVE-MANAGER", "description": "Publish the manager approve event" },
            { "code": "event.publish.EVT-REJECT", "description": "Publish the reject event" }
          ]
        }
        """;
}
