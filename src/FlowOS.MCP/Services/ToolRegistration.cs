using FlowOS.MCP.Models;
using FlowOS.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.MCP.Services;

public static class ToolRegistration
{
    public static void RegisterAll(IToolRegistry registry, IServiceProvider serviceProvider)
    {
        registry.Register("describe_workflowclass_schema", McpToolDescriptions.For("describe_workflowclass_schema"), McpToolSchemas.NoArguments(),
            async (args) => await ExecuteScopedAsync<InfoTools>(serviceProvider, t => t.DescribeSchema(args)));

        registry.Register("list_public_workflowclasses", McpToolDescriptions.For("list_public_workflowclasses"), McpToolSchemas.TenantOptional(),
            async (args) => await ExecuteScopedAsync<InfoTools>(serviceProvider, t => t.ListPublic(args)));

        registry.Register("list_notifications", McpToolDescriptions.For("list_notifications"), McpToolSchemas.ListNotifications(),
            async (args) => await ExecuteScopedAsync<NotificationTools>(serviceProvider, t => t.ListNotifications(args)));

        registry.Register("mark_notification_as_read", McpToolDescriptions.For("mark_notification_as_read"), McpToolSchemas.MarkNotificationAsRead(),
            async (args) => await ExecuteScopedAsync<NotificationTools>(serviceProvider, t => t.MarkNotificationAsRead(args)));

        registry.Register("list_available_agents", McpToolDescriptions.For("list_available_agents"), McpToolSchemas.NoArguments(),
            async (args) => await ExecuteScopedAsync<AgentTools>(serviceProvider, t => t.ListAvailableAgents(args)));

        registry.Register("suggest_agent_action", McpToolDescriptions.For("suggest_agent_action"), McpToolSchemas.SuggestAgentAction(),
            async (args) => await ExecuteScopedAsync<AgentTools>(serviceProvider, t => t.SuggestAgentAction(args)));

        registry.Register("run_agent_task", McpToolDescriptions.For("run_agent_task"), McpToolSchemas.RunAgentTask(),
            async (args) => await ExecuteScopedAsync<AgentTools>(serviceProvider, t => t.RunAgentTask(args)));

        registry.Register("get_agent_execution_history", McpToolDescriptions.For("get_agent_execution_history"), McpToolSchemas.GetAgentExecutionHistory(),
            async (args) => await ExecuteScopedAsync<AgentObservabilityMcpTools>(serviceProvider, t => t.GetAgentExecutionHistory(args)));

        registry.Register("get_agent_evaluation_metrics", McpToolDescriptions.For("get_agent_evaluation_metrics"), McpToolSchemas.GetAgentEvaluationMetrics(),
            async (args) => await ExecuteScopedAsync<AgentObservabilityMcpTools>(serviceProvider, t => t.GetAgentEvaluationMetrics(args)));

        registry.Register("get_agent_context", McpToolDescriptions.For("get_agent_context"), McpToolSchemas.GetAgentContext(),
            async (args) => await ExecuteScopedAsync<AgentContextMcpTools>(serviceProvider, t => t.GetAgentContext(args)));

        registry.Register("preview_agent_context", McpToolDescriptions.For("preview_agent_context"), McpToolSchemas.PreviewAgentContext(),
            async (args) => await ExecuteScopedAsync<AgentContextMcpTools>(serviceProvider, t => t.PreviewAgentContext(args)));

        registry.Register("upsert_agent_prompt", McpToolDescriptions.For("upsert_agent_prompt"), McpToolSchemas.UpsertAgentPrompt(),
            async (args) => await ExecuteScopedAsync<AgentContextMcpTools>(serviceProvider, t => t.UpsertAgentPrompt(args)));

        registry.Register("list_agent_prompts", McpToolDescriptions.For("list_agent_prompts"), McpToolSchemas.ListAgentPrompts(),
            async (args) => await ExecuteScopedAsync<AgentContextMcpTools>(serviceProvider, t => t.ListAgentPrompts(args)));

        registry.Register("get_agent_prompt", McpToolDescriptions.For("get_agent_prompt"), McpToolSchemas.GetAgentPrompt(),
            async (args) => await ExecuteScopedAsync<AgentContextMcpTools>(serviceProvider, t => t.GetAgentPrompt(args)));

        registry.Register("upsert_agent_provider", McpToolDescriptions.For("upsert_agent_provider"), McpToolSchemas.UpsertAgentProvider(),
            async (args) => await ExecuteScopedAsync<AgentContextMcpTools>(serviceProvider, t => t.UpsertAgentProvider(args)));

        registry.Register("list_agent_providers", McpToolDescriptions.For("list_agent_providers"), McpToolSchemas.ListAgentProviders(),
            async (args) => await ExecuteScopedAsync<AgentContextMcpTools>(serviceProvider, t => t.ListAgentProviders(args)));

        registry.Register("get_agent_provider", McpToolDescriptions.For("get_agent_provider"), McpToolSchemas.GetAgentProvider(),
            async (args) => await ExecuteScopedAsync<AgentContextMcpTools>(serviceProvider, t => t.GetAgentProvider(args)));

        registry.Register("explain_validation_violation", McpToolDescriptions.For("explain_validation_violation"), McpToolSchemas.ExplainValidationViolation(),
            async (args) => await ExecuteScopedAsync<AnalysisTools>(serviceProvider, t => t.ExplainValidationViolation(args)));

        registry.Register("lint_draft_workflowclass", McpToolDescriptions.For("lint_draft_workflowclass"), McpToolSchemas.DraftById(),
            async (args) => await ExecuteScopedAsync<AnalysisTools>(serviceProvider, t => t.LintDraftWorkflowClass(args)));

        registry.Register("simulate_workflowclass", McpToolDescriptions.For("simulate_workflowclass"), McpToolSchemas.SimulateWorkflowClass(),
            async (args) => await ExecuteScopedAsync<SimulationTools>(serviceProvider, t => t.SimulateWorkflowClass(args)));

        registry.Register("simulate_subworkflow", McpToolDescriptions.For("simulate_subworkflow"), McpToolSchemas.SimulateSubWorkflow(),
            async (args) => await ExecuteScopedAsync<SimulationTools>(serviceProvider, t => t.SimulateSubWorkflow(args)));

        registry.Register("simulate_compensation_path", McpToolDescriptions.For("simulate_compensation_path"), McpToolSchemas.SimulateCompensationPath(),
            async (args) => await ExecuteScopedAsync<SimulationTools>(serviceProvider, t => t.SimulateCompensationPath(args)));

        registry.Register("attach_step_action", McpToolDescriptions.For("attach_step_action"), McpToolSchemas.AttachStepAction(),
            async (args) => await ExecuteScopedAsync<LifecycleActionMcpTools>(serviceProvider, t => t.AttachStepAction(args)));

        registry.Register("remove_step_action", McpToolDescriptions.For("remove_step_action"), McpToolSchemas.RemoveStepAction(),
            async (args) => await ExecuteScopedAsync<LifecycleActionMcpTools>(serviceProvider, t => t.RemoveStepAction(args)));

        registry.Register("list_step_actions", McpToolDescriptions.For("list_step_actions"), McpToolSchemas.ListStepActions(),
            async (args) => await ExecuteScopedAsync<LifecycleActionMcpTools>(serviceProvider, t => t.ListStepActions(args)));

        registry.Register("register_connector", McpToolDescriptions.For("register_connector"), McpToolSchemas.RegisterConnector(),
            async (args) => await ExecuteScopedAsync<CapabilityRegistryMcpTools>(serviceProvider, t => t.RegisterCapabilityBinding(args)));

        registry.Register("list_connectors", McpToolDescriptions.For("list_connectors"), McpToolSchemas.ListConnectors(),
            async (args) => await ExecuteScopedAsync<CapabilityRegistryMcpTools>(serviceProvider, t => t.ListCapabilityBindings(args)));

        registry.Register("validate_connector", McpToolDescriptions.For("validate_connector"), McpToolSchemas.ValidateConnector(),
            async (args) => await ExecuteScopedAsync<CapabilityRegistryMcpTools>(serviceProvider, t => t.ValidateCapabilityBinding(args)));

        // Deprecated aliases kept so live tenants and saved agent scripts keep working.
        registry.Register("register_capability_binding", McpToolDescriptions.For("register_capability_binding"), McpToolSchemas.RegisterCapabilityBinding(),
            async (args) => await ExecuteScopedAsync<CapabilityRegistryMcpTools>(serviceProvider, t => t.RegisterCapabilityBinding(args)));

        registry.Register("list_capability_bindings", McpToolDescriptions.For("list_capability_bindings"), McpToolSchemas.ListCapabilityBindings(),
            async (args) => await ExecuteScopedAsync<CapabilityRegistryMcpTools>(serviceProvider, t => t.ListCapabilityBindings(args)));

        registry.Register("validate_capability_binding", McpToolDescriptions.For("validate_capability_binding"), McpToolSchemas.ValidateCapabilityBinding(),
            async (args) => await ExecuteScopedAsync<CapabilityRegistryMcpTools>(serviceProvider, t => t.ValidateCapabilityBinding(args)));

        registry.Register("register_plugin_binding", McpToolDescriptions.For("register_plugin_binding"), McpToolSchemas.RegisterPluginBinding(),
            async (args) => await ExecuteScopedAsync<PluginBindingMcpTools>(serviceProvider, t => t.RegisterPluginBinding(args)));

        registry.Register("list_plugin_bindings", McpToolDescriptions.For("list_plugin_bindings"), McpToolSchemas.ListPluginBindings(),
            async (args) => await ExecuteScopedAsync<PluginBindingMcpTools>(serviceProvider, t => t.ListPluginBindings(args)));

        registry.Register("resolve_plugin_binding", McpToolDescriptions.For("resolve_plugin_binding"), McpToolSchemas.ResolvePluginBinding(),
            async (args) => await ExecuteScopedAsync<PluginBindingMcpTools>(serviceProvider, t => t.ResolvePluginBinding(args)));

        registry.Register("create_context_binding", McpToolDescriptions.For("create_context_binding"), McpToolSchemas.CreateContextBinding(),
            async (args) => await ExecuteScopedAsync<ContextBindingMcpTools>(serviceProvider, t => t.Create(args)));

        registry.Register("update_context_binding", McpToolDescriptions.For("update_context_binding"), McpToolSchemas.UpdateContextBinding(),
            async (args) => await ExecuteScopedAsync<ContextBindingMcpTools>(serviceProvider, t => t.Update(args)));

        registry.Register("validate_context_binding", McpToolDescriptions.For("validate_context_binding"), McpToolSchemas.ContextBindingById(),
            async (args) => await ExecuteScopedAsync<ContextBindingMcpTools>(serviceProvider, t => t.Validate(args)));

        registry.Register("activate_context_binding", McpToolDescriptions.For("activate_context_binding"), McpToolSchemas.ContextBindingById(true),
            async (args) => await ExecuteScopedAsync<ContextBindingMcpTools>(serviceProvider, t => t.Activate(args)));

        registry.Register("archive_context_binding", McpToolDescriptions.For("archive_context_binding"), McpToolSchemas.ContextBindingById(true),
            async (args) => await ExecuteScopedAsync<ContextBindingMcpTools>(serviceProvider, t => t.Archive(args)));

        registry.Register("list_context_bindings", McpToolDescriptions.For("list_context_bindings"), McpToolSchemas.ListContextBindings(),
            async (args) => await ExecuteScopedAsync<ContextBindingMcpTools>(serviceProvider, t => t.List(args)));

        registry.Register("get_context_binding", McpToolDescriptions.For("get_context_binding"), McpToolSchemas.ContextBindingById(),
            async (args) => await ExecuteScopedAsync<ContextBindingMcpTools>(serviceProvider, t => t.Get(args)));

        registry.Register("simulate_context_binding", McpToolDescriptions.For("simulate_context_binding"), McpToolSchemas.SimulateContextBinding(),
            async (args) => await ExecuteScopedAsync<ContextBindingMcpTools>(serviceProvider, t => t.Simulate(args)));

        registry.Register("list_registered_plugins", McpToolDescriptions.For("list_registered_plugins"), McpToolSchemas.ListRegisteredPlugins(),
            async (args) => await ExecuteScopedAsync<PluginDiscoveryMcpTools>(serviceProvider, t => t.ListRegisteredPlugins(args)));

        registry.Register("test_action_plugin", McpToolDescriptions.For("test_action_plugin"), McpToolSchemas.TestActionPlugin(),
            async (args) => await ExecuteScopedAsync<PluginDiscoveryMcpTools>(serviceProvider, t => t.TestActionPlugin(args)));

        registry.Register("create_draft_workflowclass", McpToolDescriptions.For("create_draft_workflowclass"), McpToolSchemas.CreateDraft(),
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.CreateDraft(args)));

        registry.Register("update_draft_workflowclass", McpToolDescriptions.For("update_draft_workflowclass"), McpToolSchemas.UpdateDraft(),
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.UpdateDraft(args)));

        registry.Register("validate_draft_workflowclass", McpToolDescriptions.For("validate_draft_workflowclass"), McpToolSchemas.DraftById(),
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.ValidateDraft(args)));

        registry.Register("get_draft_workflowclass", McpToolDescriptions.For("get_draft_workflowclass"), McpToolSchemas.DraftById(),
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.GetDraft(args)));

        registry.Register("list_draft_workflowclasses", McpToolDescriptions.For("list_draft_workflowclasses"), McpToolSchemas.TenantOptional(),
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.ListDrafts(args)));

        registry.Register("generate_workflow_blueprint_from_nl", McpToolDescriptions.For("generate_workflow_blueprint_from_nl"), McpToolSchemas.GenerateBlueprintFromNaturalLanguage(),
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.GenerateBlueprintFromNaturalLanguage(args)));

        registry.Register("get_workflow_instance_status", McpToolDescriptions.For("get_workflow_instance_status"), McpToolSchemas.WorkflowInstanceStatus(),
            async (args) => await ExecuteScopedAsync<InfoTools>(serviceProvider, t => t.GetWorkflowInstanceStatus(args)));

        registry.Register("fork_public_workflowclass", McpToolDescriptions.For("fork_public_workflowclass"), McpToolSchemas.DraftById("publicId"),
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.ForkPublic(args)));

        registry.Register("publish_workflowclass", McpToolDescriptions.For("publish_workflowclass"), McpToolSchemas.PublishWorkflowClass(),
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.Publish(args)));

        registry.Register("diagnose_caller_permissions", McpToolDescriptions.For("diagnose_caller_permissions"), McpToolSchemas.DiagnoseCallerPermissions(),
            async (args) => await ExecuteScopedAsync<TenantIamMcpTools>(serviceProvider, t => t.DiagnoseCallerPermissions(args)));

        registry.Register("list_tenant_roles", McpToolDescriptions.For("list_tenant_roles"), McpToolSchemas.TenantOptional(),
            async (args) => await ExecuteScopedAsync<TenantIamMcpTools>(serviceProvider, t => t.ListTenantRoles(args)));

        registry.Register("create_tenant_role", McpToolDescriptions.For("create_tenant_role"), McpToolSchemas.CreateTenantRole(),
            async (args) => await ExecuteScopedAsync<TenantIamMcpTools>(serviceProvider, t => t.CreateTenantRole(args)));

        registry.Register("grant_role_capability", McpToolDescriptions.For("grant_role_capability"), McpToolSchemas.ChangeRoleCapability(),
            async (args) => await ExecuteScopedAsync<TenantIamMcpTools>(serviceProvider, t => t.GrantRoleCapability(args)));

        registry.Register("revoke_role_capability", McpToolDescriptions.For("revoke_role_capability"), McpToolSchemas.ChangeRoleCapability(),
            async (args) => await ExecuteScopedAsync<TenantIamMcpTools>(serviceProvider, t => t.RevokeRoleCapability(args)));

        registry.Register("start_workflow", McpToolDescriptions.For("start_workflow"), McpToolSchemas.StartWorkflow(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.StartWorkflow(args)));

        registry.Register("publish_event", McpToolDescriptions.For("publish_event"), McpToolSchemas.PublishEvent(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.PublishEvent(args)));

        registry.Register("complete_task", McpToolDescriptions.For("complete_task"), McpToolSchemas.CompleteTask(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.CompleteTask(args)));

        registry.Register("list_workflow_instances", McpToolDescriptions.For("list_workflow_instances"), McpToolSchemas.ListWorkflowInstances(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.ListWorkflowInstances(args)));

        registry.Register("get_workflow_history", McpToolDescriptions.For("get_workflow_history"), McpToolSchemas.GetWorkflowHistory(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.GetWorkflowHistory(args)));

        registry.Register("list_dead_letters", McpToolDescriptions.For("list_dead_letters"), McpToolSchemas.ListDeadLetters(),
            async (args) => await ExecuteScopedAsync<DeadLetterMcpTools>(serviceProvider, t => t.ListDeadLetters(args)));

        registry.Register("retry_dead_letter", McpToolDescriptions.For("retry_dead_letter"), McpToolSchemas.RetryDeadLetter(),
            async (args) => await ExecuteScopedAsync<DeadLetterMcpTools>(serviceProvider, t => t.RetryDeadLetter(args)));

        registry.Register("purge_dead_letter", McpToolDescriptions.For("purge_dead_letter"), McpToolSchemas.PurgeDeadLetter(),
            async (args) => await ExecuteScopedAsync<DeadLetterMcpTools>(serviceProvider, t => t.PurgeDeadLetter(args)));

        registry.Register("verify_webhook_signature", McpToolDescriptions.For("verify_webhook_signature"), McpToolSchemas.VerifyWebhookSignature(),
            async (args) => await ExecuteScopedAsync<WebhookSecurityMcpTools>(serviceProvider, t => t.VerifyWebhookSignature(args)));

        registry.Register("test_webhook_endpoint", McpToolDescriptions.For("test_webhook_endpoint"), McpToolSchemas.TestWebhookEndpoint(),
            async (args) => await ExecuteScopedAsync<WebhookSecurityMcpTools>(serviceProvider, t => t.TestWebhookEndpoint(args)));

        registry.Register("rotate_webhook_secret", McpToolDescriptions.For("rotate_webhook_secret"), McpToolSchemas.RotateWebhookSecret(),
            async (args) => await ExecuteScopedAsync<WebhookSecurityMcpTools>(serviceProvider, t => t.RotateWebhookSecret(args)));

        registry.Register("get_instance_action_history", McpToolDescriptions.For("get_instance_action_history"), McpToolSchemas.GetInstanceActionHistory(),
            async (args) => await ExecuteScopedAsync<ActionObservabilityMcpTools>(serviceProvider, t => t.GetInstanceActionHistory(args)));

        registry.Register("replay_workflow_history", McpToolDescriptions.For("replay_workflow_history"), McpToolSchemas.ReplayWorkflowHistory(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.ReplayWorkflowHistory(args)));

        registry.Register("fork_workflow_simulation", McpToolDescriptions.For("fork_workflow_simulation"), McpToolSchemas.ForkWorkflowSimulation(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.ForkWorkflowSimulation(args)));

        registry.Register("plan_workflow_compensation_path", McpToolDescriptions.For("plan_workflow_compensation_path"), McpToolSchemas.PlanWorkflowCompensationPath(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.PlanWorkflowCompensationPath(args)));

        registry.Register("register_idempotency_key", McpToolDescriptions.For("register_idempotency_key"), McpToolSchemas.RegisterIdempotencyKey(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.RegisterIdempotencyKey(args)));

        registry.Register("inspect_idempotency_status", McpToolDescriptions.For("inspect_idempotency_status"), McpToolSchemas.InspectIdempotencyStatus(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.InspectIdempotencyStatus(args)));

        registry.Register("preview_retry_policy", McpToolDescriptions.For("preview_retry_policy"), McpToolSchemas.PreviewRetryPolicy(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.PreviewRetryPolicy(args)));

        registry.Register("get_subworkflow_tree", McpToolDescriptions.For("get_subworkflow_tree"), McpToolSchemas.GetSubWorkflowTree(),
            async (args) => await ExecuteScopedAsync<ExecutionTools>(serviceProvider, t => t.GetSubWorkflowTree(args)));

        registry.Register("simulate_parallel_execution", McpToolDescriptions.For("simulate_parallel_execution"), McpToolSchemas.SimulateParallelExecution(),
            async (args) => await ExecuteScopedAsync<SimulationTools>(serviceProvider, t => t.SimulateParallelExecution(args)));

        registry.Register("refine_workflow_blueprint_from_nl", McpToolDescriptions.For("refine_workflow_blueprint_from_nl"), McpToolSchemas.RefineBlueprintFromNaturalLanguage(),
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.RefineBlueprintFromNaturalLanguage(args)));
    }

    private static async Task<CallToolResult> ExecuteScopedAsync<T>(
        IServiceProvider serviceProvider,
        Func<T, Task<CallToolResult>> action) where T : notnull
    {
        using var scope = serviceProvider.CreateScope();
        try
        {
            var tool = scope.ServiceProvider.GetRequiredService<T>();
            return await action(tool);
        }
        finally
        {
            if (!McpRequestContext.IsAuthenticatedTransport)
                McpRequestContext.TenantId = Guid.Empty;
        }
    }
}
