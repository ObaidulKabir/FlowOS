namespace FlowOS.MCP.Models;

public static class McpToolDescriptions
{
    public static IReadOnlyDictionary<string, string> All { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["describe_workflowclass_schema"] =
                "Returns the canonical camelCase JSON Schema for a WorkflowClass blueprint. " +
                "Use it before creating or updating a draft. Returns: {ok:true,data:<JSON Schema>}. " +
                "Errors: MCP-INTERNAL. Input example: {}",

            ["list_public_workflowclasses"] =
                "Lists public WorkflowClasses visible to the current tenant as id, name, and version. " +
                "HTTP uses the authenticated x-tenant-id; stdio requires tenantId. " +
                "Returns: {ok:true,data:{workflowClasses:[...]}}. " +
                "Errors: MCP-TENANT-001, MCP-TENANT-002. " +
                "Input example: {\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["list_notifications"] =
                "Lists recent notifications for the tenant and user with read status and severity. " +
                "HTTP uses the authenticated x-tenant-id; stdio requires tenantId. " +
                "Returns: {ok:true,data:{notifications:[{id,message,severity,createdAt,eventType,isRead}]}}. " +
                "Errors: MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"tenantId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\"}",

            ["mark_notification_as_read"] =
                "Marks a specific tenant/user notification as read. " +
                "HTTP uses the authenticated x-tenant-id; stdio requires tenantId. " +
                "Returns: {ok:true,data:{success:true,message:\"Notification marked as read.\"}}. " +
                "Errors: MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["list_available_agents"] =
                "Lists advisory FlowOS agents and their declared capabilities; it does not execute an agent. " +
                "Returns: {ok:true,data:{agents:[...]}}. Errors: MCP-INTERNAL. Input example: {}",

            ["suggest_agent_action"] =
                "Runs the selected advisory agent against an existing workflow instance without mutating it. " +
                "Accepts an optional `objective` string that guides the agent's analysis. " +
                "The instance lookup is tenant-scoped. HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:<SuggestedAction>}. " +
                "Errors: MCP-ARG-001, MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-NODATA-001, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"workflowInstanceId\":\"22222222-2222-2222-2222-222222222222\",\"agentId\":\"RiskAnalysisAgent\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\",\"objective\":\"Analyze expense\"}",

            ["explain_validation_violation"] =
                "Explains a FlowOS validator code and gives a design correction hint. The optional context object " +
                "can contain stepId, event, state, or capability details. " +
                "Returns: {ok:true,data:{code,humanExplanation,designHint}}. Errors: MCP-ARG-001. " +
                "Input example: {\"code\":\"CON-001\",\"context\":{\"event\":\"EVT-APPROVE\"}}",

            ["lint_draft_workflowclass"] =
                "Performs read-only advisory linting on a tenant-visible WorkflowClass; it does not replace " +
                "authoritative validation or modify the draft. HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{warnings:[{code,severity,message,context}]}}. " +
                "Errors: MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-NOTFOUND-001. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["create_draft_workflowclass"] =
                "Creates a private Draft WorkflowClass after authoritative blueprint validation; it does not publish it. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{id,tenantId,status,message}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-TENANT-002, MCP-VALIDATION, MCP-INTERNAL. " +
                "Input example: {\"name\":\"Student Leave\",\"version\":\"1.0.0\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\",\"blueprint\":{\"events\":[],\"stateMachine\":{\"initialState\":\"Draft\",\"states\":[\"Draft\"],\"transitions\":[]},\"workflow\":{\"startStepId\":\"Start\",\"steps\":[{\"stepId\":\"Start\",\"stepType\":\"End\"}]},\"roles\":[],\"capabilities\":[]}}",

            ["update_draft_workflowclass"] =
                "Replaces the blueprint of an existing tenant-owned Draft and optionally changes its name or version. " +
                "The updated blueprint must pass authoritative validation; published classes cannot be edited here. " +
                "Returns: {ok:true,data:{id,status,message}}. " +
                "Errors: MCP-ARG-001, MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-NOTFOUND-001, MCP-VALIDATION, MCP-INTERNAL. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\",\"blueprint\":{\"events\":[],\"stateMachine\":{\"initialState\":\"Draft\",\"states\":[\"Draft\"],\"transitions\":[]},\"workflow\":{\"startStepId\":\"Start\",\"steps\":[{\"stepId\":\"Start\",\"stepType\":\"End\"}]},\"roles\":[],\"capabilities\":[]}}",

            ["validate_draft_workflowclass"] =
                "Runs authoritative validation for a tenant-owned Draft without modifying or publishing it. " +
                "An invalid blueprint is a successful tool call with data.isValid=false and structured validation errors. " +
                "Returns: {ok:true,data:{isValid,errors:[{code,category,message,element}]}}. " +
                "Errors: MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["fork_public_workflowclass"] =
                "Copies a Public WorkflowClass into a new private Draft owned by the current tenant; the source is unchanged. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{id,tenantId,status,message}}. " +
                "Errors: MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"publicId\":\"44444444-4444-4444-4444-444444444444\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["get_draft_workflowclass"] =
                "Reads and returns the complete draft WorkflowClass definition, metadata, and full blueprint by ID. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:<WorkflowClassResponseDto>}. " +
                "Errors: MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["list_draft_workflowclasses"] =
                "Lists all private Draft WorkflowClasses owned by the tenant. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{drafts:[{id,name,version,status,scope,createdAt,updatedAt}]}}. " +
                "Errors: MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["get_workflow_instance_status"] =
                "Queries the runtime execution status, current step, current state, and completion timestamps of an active or completed workflow instance. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:<WorkflowSummaryDto>}. " +
                "Errors: MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"instanceId\":\"55555555-5555-5555-5555-555555555555\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["publish_workflowclass"] =
                "[Lifecycle Step 3: Publish] Publishes a tenant-owned Draft WorkflowClass, compiling it into an executable runtime WorkflowDefinition and registering associated EventDefinitions. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{id,tenantId,status,message}}. " +
                "Errors: MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-NOTFOUND-001, MCP-VALIDATION, MCP-INTERNAL. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["create_context_binding"] =
                "Creates a tenant-scoped draft context binding and immutable revision 1 from a Published or Public workflow template. " +
                "Returns: {ok:true,data:<contextBinding>}. Errors: MCP-ARG-001, MCP-NOTFOUND-001, CTX-STATE-001. " +
                "Input example: {\"sourceWorkflowClassId\":\"33333333-3333-3333-3333-333333333333\",\"contextType\":\"Expense\",\"name\":\"ExpenseApproval\",\"definition\":{\"entityType\":\"ExpenseEntity\"},\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["update_context_binding"] =
                "Updates only a draft binding revision. Updating an active binding creates its next draft revision without affecting running instances. " +
                "Returns: {ok:true,data:<contextBinding>}. Errors: MCP-ARG-001, MCP-NOTFOUND-001, CTX-STATE-001. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"definition\":{\"entityType\":\"ExpenseEntity\"},\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["validate_context_binding"] =
                "Validates source visibility, aliases, mappings, schemas, roles, capabilities, and decision providers without mutating the binding. " +
                "Returns: {ok:true,data:{isValid,errors}}. Errors: MCP-NOTFOUND-001, CTX-STATE-001. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["activate_context_binding"] =
                "Compiles and atomically activates the draft as immutable workflow, state-machine, and event definitions. Requires confirmHumanApproval=true. " +
                "Existing instances remain pinned to prior revisions. Returns: {ok:true,data:<contextBinding>}. Errors: MCP-VALIDATION, MCP-NOTFOUND-001, CTX-STATE-001, MCP-APPROVAL-REQUIRED. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"confirmHumanApproval\":true,\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["archive_context_binding"] =
                "Archives a context binding and blocks new starts while existing pinned instances continue. Requires confirmHumanApproval=true. " +
                "Returns: {ok:true,data:<contextBinding>}. Errors: MCP-NOTFOUND-001, CTX-STATE-001, MCP-APPROVAL-REQUIRED. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"confirmHumanApproval\":true,\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["list_context_bindings"] =
                "Lists tenant-scoped context bindings, optionally filtered by sourceWorkflowClassId, with active and draft revisions. " +
                "Returns: {ok:true,data:{totalCount,contextBindings}}. Errors: MCP-TENANT-001, MCP-TENANT-002. " +
                "Input example: {\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["get_context_binding"] =
                "Gets one tenant-scoped context binding and its active/draft revision details. Cross-tenant identifiers return not found. " +
                "Returns: {ok:true,data:<contextBinding>}. Errors: MCP-NOTFOUND-001, MCP-TENANT-001, MCP-TENANT-002. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["start_workflow"] =
                "[Lifecycle Step 4: Run Instance] Starts a live runtime execution instance through exactly one workflow or active context-binding selector. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{workflowInstanceId,tenantId,status,correlationId,message}}. " +
                "Errors: MCP-ARG-001, MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-NOTFOUND-001, MCP-VALIDATION, MCP-INTERNAL. " +
                "Input example: {\"contextType\":\"Expense\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\",\"payload\":{\"expense\":{\"amount\":1500}}}",

            ["publish_event"] =
                "[Lifecycle Step 5: State Transition] Publishes an event to advance the state machine and workflow step of an active workflow instance. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{success:true,workflowInstanceId,eventType,message}}. " +
                "Errors: MCP-ARG-001, MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-NOTFOUND-001, MCP-VALIDATION, MCP-EXEC-001, MCP-INTERNAL. " +
                "Input example: {\"workflowInstanceId\":\"55555555-5555-5555-5555-555555555555\",\"eventType\":\"EVT-SUBMIT\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["complete_task"] =
                "[Lifecycle Step 5: Task Execution] Completes a manual or human-in-the-loop task step within an active workflow instance. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{success:true,workflowInstanceId,taskId,message}}. " +
                "Errors: MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"workflowInstanceId\":\"55555555-5555-5555-5555-555555555555\",\"taskId\":\"66666666-6666-6666-6666-666666666666\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["list_workflow_instances"] =
                "[Telemetry] Lists active and completed workflow instances for the tenant with their current execution status and step. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{instances:[{id,workflowClassName,currentStep,status,createdAt}]}}. " +
                "Errors: MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["get_workflow_history"] =
                "[Audit & Telemetry] Retrieves the complete chronological audit trail and timeline of events for a workflow instance, " +
                "including state transitions, step advances, task completions, and AI agent insights. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{workflowInstanceId,definitionName,status,timeline:[{eventId,eventType,timestamp,summary,keyData}]}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-NOT-FOUND, MCP-INTERNAL. " +
                "Input example: {\"workflowInstanceId\":\"22222222-2222-2222-2222-222222222222\"}",

            ["simulate_workflowclass"] =
                "[Simulator] Runs a zero-side-effect, in-memory dry-run simulation of a WorkflowClass using either an existing draft/published ID or an inline blueprint. " +
                "Evaluates decision conditions against context payloads, validates state machine guards, enforces human-task role permissions, advances automated steps, " +
                "evaluates dynamic payload mappings and Handlebars templates, and injects simulated step faults via `simulateFailureAtStep` to test OnFailure Saga rollback compensation actions. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{status,workflow,initialState,finalState,initialStepId,currentStepId,totalStepsExecuted,simulatedRole,pendingHumanTask,decisionsEvaluated,stateTransitions,actionsTriggered,executionTrace,payload}}. " +
                "Errors: MCP-ARG-001, MCP-ARG-002, MCP-NOTFOUND-001, MCP-VALIDATION, MCP-INTERNAL. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"payload\":{\"Amount\":7500},\"role\":\"Director\",\"events\":[\"EVT-APPROVE\"],\"simulateFailureAtStep\":\"PaymentStep\"}",

            ["simulate_subworkflow"] =
                "[Simulator] Simulates end-to-end execution of a parent-child subworkflow relationship, including parent-to-child input parameter mapping, nested child workflow step execution, child-to-parent output mapping, and parent workflow resumption. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{parentWorkflow,subWorkflowStepId,childWorkflow,status,initialParentState,finalParentState,inputMapping,childInitialPayload,childFinalPayload,outputMapping,updatedParentPayload,childExecutionTrace,parentExecutionTrace,subworkflowsExecuted,pendingSubWorkflow,totalParentStepsExecuted}}. " +
                "Errors: MCP-ARG-001, MCP-ARG-002, MCP-NOTFOUND-001, MCP-VALIDATION, MCP-INTERNAL. " +
                "Input example: {\"parentBlueprint\":{\"Workflow\":{\"Steps\":[{\"StepId\":\"ChildStep\",\"StepType\":\"SubWorkflow\",\"SubWorkflow\":{\"WorkflowName\":\"ChildWF\",\"InputMapping\":{\"Amount\":\"OrderTotal\"},\"OutputMapping\":{\"Approved\":\"ChildApproved\"}},\"NextSteps\":{\"SubWorkflowCompleted\":\"END\"}}]}},\"childBlueprint\":{\"Workflow\":{\"StartStepId\":\"DoWork\",\"Steps\":[{\"StepId\":\"DoWork\",\"StepType\":\"Command\",\"NextSteps\":{\"Default\":\"END\"}}]}},\"payload\":{\"OrderTotal\":250}}",

            ["simulate_compensation_path"] =
                "[Saga Compensation Planner] Produces a deterministic compensation rollback path from a failed step by evaluating configured OnFailure hooks in reverse execution order (LIFO). " +
                "Supports either inline blueprint input or resolving a stored WorkflowClass by ID; reports blocked steps that have no compensation actions. " +
                "This is analysis-only and does not execute side effects. " +
                "Returns: {ok:true,data:{failedStepId,executedStepIds,isFullyCompensable,orderedCompensations,blockedSteps}}. " +
                "Errors: MCP-ARG-001, MCP-NOTFOUND-001, MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"failedStepId\":\"ReserveInventory\",\"executedStepIds\":[\"Start\",\"AuthorizePayment\",\"ReserveInventory\"],\"blueprint\":{\"events\":[],\"stateMachine\":{\"initialState\":\"Draft\",\"states\":[\"Draft\"],\"transitions\":[]},\"workflow\":{\"startStepId\":\"Start\",\"steps\":[{\"stepId\":\"Start\",\"stepType\":\"Command\"}]},\"roles\":[],\"capabilities\":[]}}",

            ["attach_step_action"] =
                "[Lifecycle Hooks] Attaches or updates a declarative lifecycle action (Webhook, Notification, PublishEvent, InvokeCapability, or plugin-prefixed alias) on a step's OnEntry, OnExit, or OnFailure (Saga rollback compensation) hook in a draft WorkflowClass, with immediate validation and persistence. " +
                "Supports Handlebars templates, dynamic LINQ payload mapping (e.g. Amount * 1.15), HMAC-SHA256 signing, and custom HTTP headers. " +
                "HTTP uses authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{stepId,hook,totalActions,actionAttached,persisted,message}}. " +
                "Errors: MCP-ARG-001, MCP-NOTFOUND-001, MCP-NOTFOUND-002, MCP-VALIDATION-FAILED, MCP-INTERNAL. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"stepId\":\"ApproveStep\",\"hook\":\"OnFailure\",\"action\":{\"actionType\":\"plugin:paymentRefunder\",\"target\":\"payment.refund.v1\"}}",

            ["remove_step_action"] =
                "[Lifecycle Hooks] Removes a declarative lifecycle action from a step's OnEntry, OnExit, or OnFailure hook in a draft WorkflowClass by index, actionType, or target. " +
                "HTTP uses authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{stepId,hook,remainingActions,persisted,message}}. " +
                "Errors: MCP-ARG-001, MCP-NOTFOUND-001, MCP-NOTFOUND-002, MCP-NOTFOUND-003, MCP-INTERNAL. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"stepId\":\"ApproveStep\",\"hook\":\"OnFailure\",\"actionIndex\":0}",

            ["list_step_actions"] =
                "[Lifecycle Hooks] Lists configured OnEntry, OnExit, and OnFailure (Saga compensation) lifecycle actions for a specific step (or all steps) in a draft or published WorkflowClass. " +
                "HTTP uses authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{workflow,stepCount,steps:[{stepId,stepType,onEntry,onExit,onFailure,totalHooks}]}}. " +
                "Errors: MCP-ARG-001, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"stepId\":\"ApproveStep\"}",

            ["register_capability_binding"] =
                "[Capability Registry] Creates or updates a tenant-scoped capability binding that maps an InvokeCapability action name to a remote endpoint contract. " +
                "Supports transport, endpoint URL, timeout, retry profile, schema versions, auth reference, and enable/disable state. " +
                "Returns: {ok:true,data:{id,tenantId,capabilityName,transport,endpointUrl,authRef,requestSchemaVersion,responseSchemaVersion,retryPolicy,timeoutMs,isEnabled,createdAtUtc,updatedAtUtc}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"capabilityName\":\"payment.refund.v1\",\"endpointUrl\":\"https://worker.example.com/capabilities/refund\",\"transport\":\"http\",\"timeoutMs\":15000,\"retryPolicy\":\"aggressive\"}",

            ["list_capability_bindings"] =
                "[Capability Registry] Lists tenant-scoped capability bindings available for InvokeCapability actions, with optional name and enabled filters. " +
                "Returns: {ok:true,data:{totalCount,bindings:[{id,capabilityName,transport,endpointUrl,timeoutMs,isEnabled,...}]}}. " +
                "Errors: MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"enabledOnly\":true}",

            ["validate_capability_binding"] =
                "[Capability Registry] Validates whether a capability binding exists, is enabled, uses a supported transport, and has a valid endpoint URL. " +
                "Returns validation status and binding metadata without mutating state. " +
                "Returns: {ok:true,data:{capabilityName,isValid,message,binding}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"capabilityName\":\"payment.refund.v1\"}",

            ["register_plugin_binding"] =
                "[Plugin Binding Registry] Creates or updates a tenant-scoped mapping from blueprint actionType/decisionProvider names to server-registered plugin providers. " +
                "Allows controlled tenant-level behavior customization while preserving server-owned plugin code and global safety flags. " +
                "Returns: {ok:true,data:{id,tenantId,bindingType,sourceName,providerName,isEnabled,createdAtUtc,updatedAtUtc}}. " +
                "Errors: MCP-ARG-001, PLUGIN-BIND-001, PLUGIN-BIND-002, PLUGIN-BIND-003, MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"bindingType\":\"action\",\"sourceName\":\"Webhook\",\"providerName\":\"Webhook\",\"isEnabled\":true}",

            ["list_plugin_bindings"] =
                "[Plugin Binding Registry] Lists tenant plugin bindings with optional filters for bindingType, sourceName, and enabled state. " +
                "Returns: {ok:true,data:{totalCount,bindings:[{id,bindingType,sourceName,providerName,isEnabled}]}}. " +
                "Errors: MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"bindingType\":\"decision\",\"enabledOnly\":true}",

            ["resolve_plugin_binding"] =
                "[Plugin Binding Registry] Resolves the effective provider for one tenant-scoped source key and reports whether the mapped provider is currently registered on the server. " +
                "Returns: {ok:true,data:{bindingType,sourceName,resolvedProvider,hasBinding,isServerRegistered}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"bindingType\":\"action\",\"sourceName\":\"Webhook\"}",

            ["list_registered_plugins"] =
                "[Plugin Discovery] Lists action and decision plugins currently registered on the server runtime so AI agents can design with real provider names instead of guessing. " +
                "Includes wildcard/strict-mode runtime policy flags and plugin alias conventions. " +
                "Returns: {ok:true,data:{totalActionPlugins,totalDecisionPlugins,actionPlugins,decisionPlugins,conventions,runtimePolicy}}. " +
                "Errors: MCP-INTERNAL. " +
                "Input example: {\"includeWildcard\":true}",

            ["test_action_plugin"] =
                "[Plugin Testing & Validation] Tests or dry-runs an action plugin (e.g. Email, Slack, WhatsApp, Webhook, Notification, PublishEvent, InvokeCapability) by building its structured outbox message and validating required channel parameters without executing side effects or persisting records. " +
                "Returns: {ok:true,data:{actionType,messageType,isRegistered,status,builtPayload,diagnostics:[...]}}. " +
                "Errors: MCP-ARG-001, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"actionType\":\"Email\",\"target\":\"ops@company.com\",\"template\":\"Alert: High Latency\",\"payload\":{\"to\":\"ops@company.com\",\"subject\":\"Alert: High Latency\",\"body\":\"Service latency exceeded threshold.\"}}",

            ["list_dead_letters"] =
                "[Resilience & DLQ] Lists dead-lettered outbox messages that exhausted all retry attempts (e.g. downstream 5xx errors or persistent network timeouts). " +
                "Returns failure error strings, target endpoints, attempt counts, and serialized payload details so autonomous AI agents can diagnose outages. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{totalCount,deadLetters:[{id,tenantId,type,occurredOnUtc,retryCount,maxRetries,error,actionType,targetUrl,httpMethod,stepId,payload}]}}. " +
                "Errors: MCP-INTERNAL. " +
                "Input example: {\"limit\":20,\"type\":\"WorkflowAction:Webhook\"}",

            ["retry_dead_letter"] =
                "[Resilience & DLQ] Resets retry counts and immediately re-enqueues one specific dead letter or all dead letters for the tenant back into the active Outbox. " +
                "Use this tool when a downstream API has recovered to resume processing without manual engineer intervention. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{success:true,id,replayedCount,message}}. " +
                "Errors: MCP-ARG-001, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"id\":\"77777777-7777-7777-7777-777777777777\"}",

            ["purge_dead_letter"] =
                "[Resilience & DLQ] Permanently deletes an unrecoverable dead letter from the transactional outbox after diagnosis to maintain queue hygiene. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{success:true,id,message}}. " +
                "Errors: MCP-ARG-001, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"id\":\"77777777-7777-7777-7777-777777777777\"}",

            ["verify_webhook_signature"] =
                "[Webhook Security] Validates or generates bank-grade HMAC-SHA256 signatures (X-FlowOS-Signature: t={ts},v1={hash}) for a given payload string and signing secret. " +
                "Use this tool to verify webhook authenticity, test receiver validation logic, and ensure non-repudiation. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{isValid,signature,signatureHeader,timestamp,hash,algorithm,message}}. " +
                "Errors: MCP-ARG-001, MCP-INTERNAL. " +
                "Input example: {\"payload\":\"{\\\"orderId\\\":\\\"123\\\"}\",\"secret\":\"whsec_demo_secret\"}",

            ["test_webhook_endpoint"] =
                "[Webhook Security] Dispatches a live pre-flight probe ping to an external HTTP endpoint with full cryptographic headers (X-FlowOS-Signature, timestamp, delivery id). " +
                "Measures latency in milliseconds, reports HTTP status codes, and returns server response snippets before activating production webhooks. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{targetUrl,httpMethod,statusCode,statusText,latencyMs,isSuccess,signatureSent,timestampSent,responseSnippet}}. " +
                "Errors: MCP-ARG-001, MCP-EXEC-001, MCP-INTERNAL. " +
                "Input example: {\"url\":\"https://httpbin.org/post\",\"method\":\"POST\"}",

            ["rotate_webhook_secret"] =
                "[Webhook Security] Zero-downtime key rotation: generates a fresh cryptographic 32-byte HMAC-SHA256 signing secret for the tenant. " +
                "Existing webhooks will immediately sign using the new secret key. " +
                "HTTP uses authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{success:true,tenantId,webhookSigningSecret,message}}. " +
                "Errors: MCP-ARG-001, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["get_instance_action_history"] =
                "[Observability & Forensics] Retrieves the complete immutable execution audit log of lifecycle actions (built-ins plus plugin aliases after binding resolution) for a specific workflow instance. " +
                "Includes microsecond duration (durationMs), HTTP status codes, request and response snippets, retry attempt counts, and error diagnostics for root-cause analysis. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{workflowInstanceId,totalActions,actions:[{id,stepId,triggerPhase,actionType,target,status,executedAtUtc,durationMs,httpStatusCode,requestPayloadSnippet,responseSnippet,errorMessage,attemptNumber}]}}. " +
                "Errors: MCP-ARG-001, MCP-INTERNAL. " +
                "Input example: {\"workflowInstanceId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["generate_workflow_blueprint_from_nl"] =
                "[AI Copilot & Synthesis] Generates a complete, compliant WorkflowClassBlueprint from natural language instructions (or refines an existing blueprint). " +
                "Automatically configures pure State Machine lifecycles, procedural steps, parallel Fork/Join execution branches, Decision rules, SLA timers with intermediate countdown reminders, relative pre/post-event lead-time timers, and Outbox notification hooks. " +
                "Returns: {ok:true,data:{suggestedName,suggestedVersion,summary,explanation,blueprint,validation}}. " +
                "Errors: MCP-ARG-001, MCP-INTERNAL. " +
                "Input example: {\"prompt\":\"Appointment booking with reminder 24h before appointmentDate, and 48h approval SLA with reminder 2h before timeout.\"}",

            ["replay_workflow_history"] =
                "[Time-Travel Debugging] Reconstructs a read-only chronological replay timeline from the immutable DomainEvent stream for a workflow instance. " +
                "Each snapshot includes active tokens, legal state, variable mutations, and correlated lifecycle actions. Does not mutate production state. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{workflowInstanceId,workflowClassName,status,totalSteps,snapshots:[{stepIndex,eventType,fromStepId,toStepId,activeStepIds,fromState,toState,variables,actionLogs,summary}]}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"workflowInstanceId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["fork_workflow_simulation"] =
                "[Time-Travel Debugging] Evaluates a sandboxed what-if branch from a historical snapshot without writing to the live instance, audit log, or Outbox. " +
                "Clones state at targetStepIndex, applies alternativeEvent/payload through the workflow engine, and returns the projected next step and state. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{forkFromStepIndex,baseStepId,baseState,alternativeEvent,projectedStepId,projectedState,isAllowed,reason,projectedActions,sideEffects}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-INTERNAL. " +
                "Input example: {\"workflowInstanceId\":\"11111111-1111-1111-1111-111111111111\",\"targetStepIndex\":1,\"alternativeEvent\":\"EVT-REJECT\"}",

            ["plan_workflow_compensation_path"] =
                "[Resilience & Saga Analysis] Computes an execution-aware compensation rollback path for a workflow instance using immutable replay history and configured OnFailure hooks. " +
                "Returns compensations in reverse execution order and flags blocked steps that lack compensation actions. Analysis-only; no side effects are executed. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{workflowInstanceId,failedStepId,executedStepIds,isFullyCompensable,orderedCompensations,blockedSteps}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"workflowInstanceId\":\"11111111-1111-1111-1111-111111111111\",\"failedStepId\":\"ReserveInventory\"}",

            ["register_idempotency_key"] =
                "[Reliability & Exactly-Once] Reserves a tenant-scoped idempotency key for a named operation before executing mutating calls. " +
                "If the key already exists, returns registered=false. Useful for distributed callers that need deterministic dedup semantics. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{operationName,idempotencyKey,registered,status}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-INTERNAL. " +
                "Input example: {\"operationName\":\"publish_event\",\"idempotencyKey\":\"ord-2026-09-13-001\"}",

            ["inspect_idempotency_status"] =
                "[Reliability & Exactly-Once] Retrieves the status of a tenant-scoped idempotency key for a named operation. " +
                "Status transitions: Pending, Completed, Failed. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{tenantId,operationName,idempotencyKey,status,createdAtUtc,updatedAtUtc}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"operationName\":\"publish_event\",\"idempotencyKey\":\"ord-2026-09-13-001\"}",

            ["preview_retry_policy"] =
                "[Reliability & Resilience] Previews retry scheduling behavior (exponential, linear, constant) and classifies error strings as transient or permanent. " +
                "Helps callers tune retry policy before executing mutating operations. " +
                "Returns: {ok:true,data:{strategy,maxRetries,currentRetryCount,baseDelaySeconds,maxDelaySeconds,shouldRetryNow,classification,attempts,previewGeneratedAtUtc}}. " +
                "Errors: MCP-INTERNAL. " +
                "Input example: {\"currentRetryCount\":1,\"maxRetries\":5,\"baseDelaySeconds\":2,\"strategy\":\"exponential\",\"errorMessage\":\"HTTP 503 timeout\"}",

            ["get_subworkflow_tree"] =
                "[Hierarchical Workflows] Reconstructs the parent-child subworkflow execution hierarchy tree for a root or parent workflow instance. " +
                "Returns the root node along with nested child workflow instances, current step, state machine status, and triggering parent step ID. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{workflowInstanceId,workflowClassName,status,currentStepId,currentState,hasChildren,childCount,children:[{workflowInstanceId,workflowClassName,parentStepId,status,currentStepId,currentState}]}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"workflowInstanceId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["simulate_parallel_execution"] =
                "[Parallel Execution Simulation] Dry-runs parallel execution branches spawned by a Fork step to a Join barrier synchronization gateway. " +
                "Simulates concurrent branch progression, evaluates step actions, and validates Join barrier policies without mutating production state. " +
                "HTTP uses authenticated tenant; stdio accepts tenantId. " +
                "Returns: {ok:true,data:{workflow,forkStepId,totalBranches,branches:[...],joinStepId,joinPolicy,isSynchronized,resumedStepId,actionsTriggeredCount,executionTrace:[...]}}. " +
                "Errors: MCP-ARG-001, MCP-TENANT-001, MCP-NOTFOUND-001, MCP-INTERNAL. " +
                "Input example: {\"id\":\"33333333-3333-3333-3333-333333333333\",\"payload\":{\"loanAmount\":50000}}",

            ["refine_workflow_blueprint_from_nl"] =
                "[AI Copilot & Blueprint Refinement] Incrementally refines and enriches an existing WorkflowClassBlueprint using natural language instructions. " +
                "Allows modifying steps, adding parallel branches, inserting SLA timers with intermediate countdown reminders, adding relative pre/post-event timers, or attaching webhook and compensation actions while maintaining schema compliance. " +
                "Returns: {ok:true,data:{suggestedName,suggestedVersion,summary,explanation,blueprint,validation}}. " +
                "Errors: MCP-ARG-001, MCP-INTERNAL. " +
                "Input example: {\"prompt\":\"Add 24h SLA timeout with a reminder 2h before due time, and manager escalation webhook.\",\"currentBlueprint\":{\"events\":[],\"stateMachine\":{\"initialState\":\"Draft\",\"states\":[\"Draft\"],\"transitions\":[]},\"workflow\":{\"startStepId\":\"Start\",\"steps\":[{\"stepId\":\"Start\",\"stepType\":\"End\"}]},\"roles\":[],\"capabilities\":[]}}"
        };

    public static string For(string toolName) =>
        All.TryGetValue(toolName, out var description)
            ? description
            : throw new ArgumentOutOfRangeException(nameof(toolName), toolName, "Unknown MCP tool.");

    public static readonly IReadOnlyDictionary<string, ToolSecurityProfile> SecurityProfiles =
        new Dictionary<string, ToolSecurityProfile>(StringComparer.Ordinal)
        {
            ["get_subworkflow_tree"] = new("query", "authenticated", true, true, false, "none", true, "low"),
            ["simulate_parallel_execution"] = new("analysis", "authenticated", true, false, false, "none", false, "low"),
            ["refine_workflow_blueprint_from_nl"] = new("governance", "authenticated", true, true, false, "none", true, "low"),
            ["generate_workflow_blueprint_from_nl"] = new("governance", "authenticated", true, true, false, "none", true, "low"),
            ["replay_workflow_history"] = new("observability", "authenticated", true, true, false, "none", true, "low"),
            ["fork_workflow_simulation"] = new("analysis", "authenticated", true, true, false, "none", true, "low"),
            ["plan_workflow_compensation_path"] = new("analysis", "authenticated", true, true, false, "none", true, "low"),
            ["register_idempotency_key"] = new("reliability", "authenticated", true, true, true, "reversible", true, "low"),
            ["inspect_idempotency_status"] = new("reliability", "authenticated", true, true, false, "none", true, "low"),
            ["preview_retry_policy"] = new("reliability", "authenticated", true, false, false, "none", false, "low"),
            ["simulate_compensation_path"] = new("analysis", "authenticated", true, false, false, "none", false, "low"),
            ["describe_workflowclass_schema"] = new("info", "public", false, false, false, "none", false, "low"),
            ["explain_validation_violation"] = new("analysis", "public", false, false, false, "none", false, "low"),
            ["list_available_agents"] = new("analysis", "authenticated", true, false, false, "none", false, "low"),
            ["suggest_agent_action"] = new("analysis", "authenticated", true, true, false, "none", true, "low"),
            ["lint_draft_workflowclass"] = new("analysis", "authenticated", true, true, false, "none", true, "low"),
            ["simulate_workflowclass"] = new("analysis", "authenticated", true, false, false, "none", false, "low"),
            ["simulate_subworkflow"] = new("analysis", "authenticated", true, false, false, "none", false, "low"),
            ["attach_step_action"] = new("governance", "authenticated", true, true, true, "reversible", true, "low"),
            ["remove_step_action"] = new("governance", "authenticated", true, true, true, "reversible", true, "low"),
            ["list_step_actions"] = new("query", "authenticated", true, true, false, "none", true, "low"),
            ["register_capability_binding"] = new("integration", "authenticated", true, true, true, "reversible", true, "medium"),
            ["list_capability_bindings"] = new("integration", "authenticated", true, true, false, "none", true, "low"),
            ["validate_capability_binding"] = new("integration", "authenticated", true, true, false, "none", true, "low"),
            ["register_plugin_binding"] = new("integration", "authenticated", true, true, true, "reversible", true, "medium"),
            ["list_plugin_bindings"] = new("integration", "authenticated", true, true, false, "none", true, "low"),
            ["resolve_plugin_binding"] = new("integration", "authenticated", true, true, false, "none", true, "low"),
            ["list_registered_plugins"] = new("integration", "authenticated", true, false, false, "none", false, "low"),
            ["test_action_plugin"] = new("analysis", "authenticated", true, false, false, "none", false, "low"),
            ["list_dead_letters"] = new("resilience", "authenticated", true, true, false, "none", true, "low"),
            ["retry_dead_letter"] = new("resilience", "authenticated", true, true, true, "reversible", true, "low"),
            ["purge_dead_letter"] = new("resilience", "authenticated", true, true, true, "irreversible", true, "medium"),
            ["verify_webhook_signature"] = new("security", "authenticated", true, true, false, "none", true, "low"),
            ["test_webhook_endpoint"] = new("security", "authenticated", true, true, true, "reversible", true, "medium"),
            ["rotate_webhook_secret"] = new("security", "authenticated", true, true, true, "irreversible", true, "high"),
            ["get_instance_action_history"] = new("observability", "authenticated", true, true, false, "none", true, "low"),
            ["list_public_workflowclasses"] = new("query", "authenticated", true, true, false, "none", true, "low"),
            ["get_workflow_instance_status"] = new("query", "authenticated", true, true, false, "none", true, "low"),
            ["list_workflow_instances"] = new("query", "authenticated", true, true, false, "none", true, "low"),
            ["get_workflow_history"] = new("query", "authenticated", true, true, false, "none", true, "low"),
            ["list_notifications"] = new("notification", "authenticated", true, true, false, "none", true, "low"),
            ["mark_notification_as_read"] = new("command", "authenticated", true, true, true, "reversible", true, "low"),
            ["create_draft_workflowclass"] = new("governance", "authenticated", true, true, true, "reversible", true, "low"),
            ["update_draft_workflowclass"] = new("governance", "authenticated", true, true, true, "reversible", true, "low"),
            ["validate_draft_workflowclass"] = new("governance", "authenticated", true, true, false, "none", true, "low"),
            ["get_draft_workflowclass"] = new("governance", "authenticated", true, true, false, "none", true, "low"),
            ["list_draft_workflowclasses"] = new("governance", "authenticated", true, true, false, "none", true, "low"),
            ["fork_public_workflowclass"] = new("governance", "authenticated", true, true, true, "reversible", true, "low"),
            ["publish_workflowclass"] = new("governance", "authenticated", true, true, true, "irreversible", true, "high", true),
            ["create_context_binding"] = new("governance", "authenticated", true, true, true, "reversible", true, "low"),
            ["update_context_binding"] = new("governance", "authenticated", true, true, true, "reversible", true, "low"),
            ["validate_context_binding"] = new("governance", "authenticated", true, true, false, "none", true, "low"),
            ["activate_context_binding"] = new("governance", "authenticated", true, true, true, "irreversible", true, "high", true),
            ["archive_context_binding"] = new("governance", "authenticated", true, true, true, "irreversible", true, "high", true),
            ["list_context_bindings"] = new("governance", "authenticated", true, true, false, "none", true, "low"),
            ["get_context_binding"] = new("governance", "authenticated", true, true, false, "none", true, "low"),
            ["start_workflow"] = new("command", "authenticated", true, true, true, "irreversible", true, "medium"),
            ["publish_event"] = new("command", "authenticated", true, true, true, "irreversible", true, "medium"),
            ["complete_task"] = new("command", "authenticated", true, true, true, "irreversible", true, "medium")
        };

    public static ToolSecurityProfile ProfileFor(string toolName) =>
        SecurityProfiles.TryGetValue(toolName, out var profile)
            ? profile
            : new ToolSecurityProfile("unknown", "authenticated", true, true, false, "none", true, "medium", false);
}

public record ToolSecurityProfile(
    string Category,
    string Access,
    bool AuthenticationRequired,
    bool AuthorizationRequired,
    bool Mutating,
    string SideEffect,
    bool TenantScoped,
    string RiskLevel,
    bool RequiresHumanConfirmation = false
);
