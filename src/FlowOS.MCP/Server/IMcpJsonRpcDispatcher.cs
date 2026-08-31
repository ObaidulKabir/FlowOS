using FlowOS.MCP.Models;

namespace FlowOS.MCP.Server;

public enum McpDispatchKind
{
    InvalidJson,
    NoResponse,
    Response
}

public sealed record McpDispatchOutcome(McpDispatchKind Kind, JsonRpcResponse? Response = null);

public interface IMcpJsonRpcDispatcher
{
    Task<McpDispatchOutcome> DispatchAsync(string json, CancellationToken cancellationToken = default);
}
