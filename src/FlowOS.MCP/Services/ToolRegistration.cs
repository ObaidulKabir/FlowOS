using FlowOS.MCP.Models;
using FlowOS.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.MCP.Services;

public static class ToolRegistration
{
    public static void RegisterAll(IToolRegistry registry, IServiceProvider serviceProvider)
    {
        registry.Register("describe_workflowclass_schema", "Get the JSON schema for WorkflowClassBlueprint", null,
            async (args) => await ExecuteScopedAsync<InfoTools>(serviceProvider, t => t.DescribeSchema(args)));

        registry.Register("list_public_workflowclasses", "List all Public WorkflowClasses", null,
            async (args) => await ExecuteScopedAsync<InfoTools>(serviceProvider, t => t.ListPublic(args)));

        registry.Register("list_available_agents", "List all available AI Agents and their capabilities", null,
            async (args) => await ExecuteScopedAsync<AgentTools>(serviceProvider, t => t.ListAvailableAgents(args)));

        registry.Register("suggest_agent_action", "Get suggested actions from an agent for a workflow instance", null,
            async (args) => await ExecuteScopedAsync<AgentTools>(serviceProvider, t => t.SuggestAgentAction(args)));

        registry.Register("explain_validation_violation", "Explain a validation error code and provide hints", null,
            async (args) => await ExecuteScopedAsync<AnalysisTools>(serviceProvider, t => t.ExplainValidationViolation(args)));

        registry.Register("lint_draft_workflowclass", "Analyze a Draft for design quality warnings", null,
            async (args) => await ExecuteScopedAsync<AnalysisTools>(serviceProvider, t => t.LintDraftWorkflowClass(args)));

        registry.Register("create_draft_workflowclass", "Create a new Draft WorkflowClass", null,
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.CreateDraft(args)));

        registry.Register("update_draft_workflowclass", "Update a Draft WorkflowClass", null,
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.UpdateDraft(args)));

        registry.Register("validate_draft_workflowclass", "Validate a Draft WorkflowClass", null,
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.ValidateDraft(args)));

        registry.Register("fork_public_workflowclass", "Fork a Public WorkflowClass to a new Draft", null,
            async (args) => await ExecuteScopedAsync<GovernanceTools>(serviceProvider, t => t.ForkPublic(args)));
    }

    private static async Task<CallToolResult> ExecuteScopedAsync<T>(
        IServiceProvider serviceProvider,
        Func<T, Task<CallToolResult>> action) where T : notnull
    {
        using var scope = serviceProvider.CreateScope();
        var tool = scope.ServiceProvider.GetRequiredService<T>();
        return await action(tool);
    }
}
