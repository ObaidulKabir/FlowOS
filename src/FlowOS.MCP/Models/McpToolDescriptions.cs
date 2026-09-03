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

            ["start_workflow"] =
                "[Lifecycle Step 4: Run Instance] Starts a live runtime execution instance of a published WorkflowClass or WorkflowDefinition. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{workflowInstanceId,tenantId,status,correlationId,message}}. " +
                "Errors: MCP-ARG-001, MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-INTERNAL. " +
                "Input example: {\"workflowClassId\":\"33333333-3333-3333-3333-333333333333\",\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}",

            ["publish_event"] =
                "[Lifecycle Step 5: State Transition] Publishes an event to advance the state machine and workflow step of an active workflow instance. " +
                "HTTP uses the authenticated tenant; stdio requires tenantId. " +
                "Returns: {ok:true,data:{success:true,workflowInstanceId,eventType,message}}. " +
                "Errors: MCP-ARG-001, MCP-ARG-002, MCP-TENANT-001, MCP-TENANT-002, MCP-EXEC-001, MCP-INTERNAL. " +
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
                "Input example: {\"tenantId\":\"11111111-1111-1111-1111-111111111111\"}"
        };

    public static string For(string toolName) =>
        All.TryGetValue(toolName, out var description)
            ? description
            : throw new ArgumentOutOfRangeException(nameof(toolName), toolName, "Unknown MCP tool.");
}
