using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Services;

public sealed class McpToolException : Exception
{
    public McpToolException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public static class McpTenantResolver
{
    public static Guid ResolveRequired(JObject args)
    {
        var contextTenant = McpRequestContext.TenantId;
        var argumentText = args["tenantId"]?.ToString();
        var hasArgument = !string.IsNullOrWhiteSpace(argumentText);

        Guid argumentTenant = Guid.Empty;
        if (hasArgument && !Guid.TryParse(argumentText, out argumentTenant))
            throw new McpToolException("MCP-ARG-002", "tenantId must be a valid UUID.");

        if (McpRequestContext.IsAuthenticatedTransport)
        {
            if (contextTenant == Guid.Empty)
                throw new McpToolException("MCP-TENANT-001", "Authenticated tenant context is missing.");
            if (argumentTenant != Guid.Empty && argumentTenant != contextTenant)
                throw new McpToolException("MCP-TENANT-002", "tenantId does not match the authenticated tenant.");

            return contextTenant;
        }

        if (argumentTenant != Guid.Empty)
        {
            McpRequestContext.TenantId = argumentTenant;
            return argumentTenant;
        }

        throw new McpToolException(
            "MCP-TENANT-001",
            "tenantId is required through the authenticated HTTP context or tool arguments.");
    }
}
