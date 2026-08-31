using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Server;

public sealed class McpJsonRpcDispatcher : IMcpJsonRpcDispatcher
{
    private readonly IToolRegistry _toolRegistry;

    public McpJsonRpcDispatcher(IToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public async Task<McpDispatchOutcome> DispatchAsync(string json, CancellationToken cancellationToken = default)
    {
        JsonRpcRequest? request;
        try
        {
            request = JsonConvert.DeserializeObject<JsonRpcRequest>(json);
        }
        catch
        {
            return new McpDispatchOutcome(McpDispatchKind.InvalidJson);
        }

        if (request == null)
            return new McpDispatchOutcome(McpDispatchKind.InvalidJson);

        // Notifications (no id) that we recognize — no response body.
        if (string.Equals(request.Method, "notifications/initialized", StringComparison.Ordinal))
            return new McpDispatchOutcome(McpDispatchKind.NoResponse);

        object? result = null;
        JsonRpcError? error = null;

        try
        {
            switch (request.Method)
            {
                case "initialize":
                    result = new
                    {
                        protocolVersion = "2025-03-26",
                        capabilities = new
                        {
                            tools = new { listChanged = false }
                        },
                        serverInfo = new
                        {
                            name = "FlowOS MCP Server",
                            version = "1.0.0"
                        }
                    };
                    break;

                case "tools/list":
                    result = new { tools = _toolRegistry.GetTools() };
                    break;

                case "tools/call":
                    if (request.Params == null)
                        throw new ArgumentException("Params required");
                    var callParams = request.Params.ToObject<CallToolParams>();
                    if (callParams == null)
                        throw new ArgumentException("Invalid params");

                    result = await _toolRegistry.ExecuteAsync(
                        callParams.Name,
                        callParams.Arguments ?? new JObject());
                    break;

                default:
                    if (request.Id != null)
                        error = new JsonRpcError { Code = -32601, Message = "Method not found" };
                    else
                        return new McpDispatchOutcome(McpDispatchKind.NoResponse);
                    break;
            }
        }
        catch (Exception ex)
        {
            error = new JsonRpcError { Code = -32000, Message = ex.Message };
        }

        if (request.Id == null)
            return new McpDispatchOutcome(McpDispatchKind.NoResponse);

        return new McpDispatchOutcome(
            McpDispatchKind.Response,
            new JsonRpcResponse
            {
                Id = request.Id,
                Result = result,
                Error = error
            });
    }
}
