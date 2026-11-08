using FlowOS.MCP.Models;
using FlowOS.MCP.Services;

namespace FlowOS.MCP.UnitTests;

/// <summary>
/// Applications (1/4-4/4) follow-up: the MCP tool surface has no applicationId support anywhere
/// outside the agent-context tools (upsert_agent_prompt/profile and their list/get variants, from
/// step 3/4). These tests pin the guidance text that says so, so a future change that actually adds
/// applicationId support to one of these tools is forced to update this documentation in the same PR
/// (AGENTS.md rule 7), instead of leaving it stale.
/// </summary>
public class ApplicationScopeGuidanceTests
{
    [Fact]
    public void BoundedAutonomyTasksGuide_application_scope_bullet_says_governance_tools_have_no_applicationId_support()
    {
        var guide = FlowOsMcpGuidance.BoundedAutonomyTasksGuide;

        // Must say plainly that this is an absence of support, not merely an unenforced check.
        Assert.Contains("genuinely absent", guide);

        // Must name every tool family this applies to, not just GovernanceTools.
        Assert.Contains("create_draft_workflowclass", guide);
        Assert.Contains("list_draft_workflowclasses", guide);
        Assert.Contains("lint_draft_workflowclass", guide);
        Assert.Contains("list_public_workflowclasses", guide);
        Assert.Contains("simulate_workflowclass", guide);
        Assert.Contains("attach_step_action", guide);

        // Must say there is no MCP tool for TenantApplication CRUD/assignment/key issuance at all.
        Assert.Contains("no MCP tool to create, list, update, or delete a `TenantApplication`", guide);
    }

    [Fact]
    public void CreateDraftWorkflowClass_description_warns_it_cannot_set_applicationId()
    {
        var description = McpToolDescriptions.For("create_draft_workflowclass");

        Assert.Contains("no applicationId argument", description);
        Assert.Contains("unassigned", description);
    }

    [Fact]
    public void ListDraftWorkflowClasses_description_warns_it_ignores_application_scope()
    {
        var description = McpToolDescriptions.For("list_draft_workflowclasses");

        Assert.Contains("no applicationId filter", description);
    }
}
