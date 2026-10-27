using FlowOS.MCP.Services;

namespace FlowOS.MCP.UnitTests;

public class IntegrationGuidanceTests
{
    [Fact]
    public void Agents_are_told_how_to_link_trace_retry_and_read_changes()
    {
        var instructions = FlowOsMcpGuidance.SystemInstructions;

        Assert.Contains("businessReference", instructions);
        Assert.Contains("externalReference", instructions);
        Assert.Contains("idempotencyKey", instructions);
        Assert.Contains("causationId", instructions);
        Assert.Contains("MCP-CONFLICT-001", instructions);
        Assert.Contains("Do not retry with a new key", instructions);
        Assert.Contains("read_change_feed", instructions);
    }
}
