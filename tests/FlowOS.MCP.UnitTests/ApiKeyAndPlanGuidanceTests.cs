using FlowOS.MCP.Models;
using FlowOS.MCP.Services;

namespace FlowOS.MCP.UnitTests;

public class ApiKeyAndPlanGuidanceTests
{
    [Fact]
    public void Agents_AreToldThat402IsNotKeyExpiry_AndNeverExpireKeysStayValid()
    {
        var instructions = FlowOsMcpGuidance.SystemInstructions;
        Assert.Contains("flw_live_", instructions);
        Assert.Contains("does not expire", instructions);
        Assert.Contains("Authorization: Bearer", instructions);
        Assert.Contains("JWTs use the caller's tenant, role, and identity", instructions);
        Assert.Contains("24 hours", instructions);
        Assert.Contains("MCP-PLAN-REQUIRED", instructions);
        Assert.Contains("That 402 is not key expiry", instructions);
        Assert.Contains("Cross-tenant access forbidden", instructions);
        Assert.Contains("Starter through Scale are stored as Managed", instructions);

        Assert.Contains("MCP-PLAN-REQUIRED", McpToolDescriptions.For("start_workflow"));
        Assert.Contains("not an expired flw_live_ key", McpToolDescriptions.For("publish_event"));
        Assert.Contains("MCP-PLAN-REQUIRED", McpToolDescriptions.For("complete_task"));
        Assert.Contains("request_plan_activation", instructions);
        Assert.Contains("request_plan_activation", McpToolDescriptions.For("start_workflow"));
        Assert.Contains("never activates", McpToolDescriptions.For("request_plan_activation"));
    }
}
