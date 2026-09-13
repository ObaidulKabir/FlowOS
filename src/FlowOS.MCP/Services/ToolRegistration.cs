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

        registry.Register("explain_validation_violation", McpToolDescriptions.For("explain_validation_violation"), McpToolSchemas.ExplainValidationViolation(),
            async (args) => await ExecuteScopedAsync<AnalysisTools>(serviceProvider, t => t.ExplainValidationViolation(args)));

        registry.Register("lint_draft_workflowclass", McpToolDescriptions.For("lint_draft_workflowclass"), McpToolSchemas.DraftById(),
            async (args) => await ExecuteScopedAsync<AnalysisTools>(serviceProvider, t => t.LintDraftWorkflowClass(args)));

        registry.Register("simulate_workflowclass", McpToolDescriptions.For("simulate_workflowclass"), McpToolSchemas.SimulateWorkflowClass(),
            async (args) => await ExecuteScopedAsync<SimulationTools>(serviceProvider, t => t.SimulateWorkflowClass(args)));

        registry.Register("attach_step_action", McpToolDescriptions.For("attach_step_action"), McpToolSchemas.AttachStepAction(),
            async (args) => await ExecuteScopedAsync<LifecycleActionMcpTools>(serviceProvider, t => t.AttachStepAction(args)));

        registry.Register("remove_step_action", McpToolDescriptions.For("remove_step_action"), McpToolSchemas.RemoveStepAction(),
            async (args) => await ExecuteScopedAsync<LifecycleActionMcpTools>(serviceProvider, t => t.RemoveStepAction(args)));

        registry.Register("list_step_actions", McpToolDescriptions.For("list_step_actions"), McpToolSchemas.ListStepActions(),
            async (args) => await ExecuteScopedAsync<LifecycleActionMcpTools>(serviceProvider, t => t.ListStepActions(args)));

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

        registry.Register("get_workflow_instance_status", McpToolDescriptions.For("get_workflow_instance_status"), McpToolSchemas.WorkflowInstanceStatus(),
            async (args) => await ExecuteScopedAsync<InfoTools>(serviceProvider, t => t.GetWorkflowInstanceStatus(args)));

        registry.Register("fork_public_workflowclass", McpToolDescriptions.For("fork_public_workflowclass"), McpToolSchemas.DraftById("publicId"),
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.ForkPublic(args)));

        registry.Register("publish_workflowclass", McpToolDescriptions.For("publish_workflowclass"), McpToolSchemas.PublishWorkflowClass(),
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.Publish(args)));

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
