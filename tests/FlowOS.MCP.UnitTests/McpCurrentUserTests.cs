using FlowOS.MCP.Services;

namespace FlowOS.MCP.UnitTests;

public class McpCurrentUserTests
{
    [Fact]
    public void Jwt_caller_id_is_used_only_for_the_current_request()
    {
        var user = new McpCurrentUser();
        try
        {
            McpRequestContext.CallerId = "jwt-user-id";
            McpRequestContext.Role = "Tenant";

            Assert.Equal("jwt-user-id", user.Id);
            Assert.Equal(["Tenant"], user.Roles);
        }
        finally
        {
            McpRequestContext.Clear();
        }

        Assert.Equal("mcp-agent", user.Id);
        Assert.Empty(user.Roles);
    }
}
