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
