using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Server;

public sealed class McpJsonRpcDispatcher : IMcpJsonRpcDispatcher
{
    public const string SupportedProtocolVersion = "2025-03-26";
    private readonly IToolRegistry _toolRegistry;

    public McpJsonRpcDispatcher(IToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public async Task<McpDispatchOutcome> DispatchAsync(string json, CancellationToken cancellationToken = default)
    {
        JToken token;
        try
        {
            token = JToken.Parse(json);
        }
        catch
        {
            return Response(Error(null, -32700, "Parse error"));
        }

        if (token is JArray batch)
        {
            if (batch.Count == 0)
                return Response(Error(null, -32600, "Invalid Request"));
            if (batch.OfType<JObject>().Any(item => item["method"]?.ToString() == "initialize"))
                return Response(Error(null, -32600, "initialize must not be sent in a batch"));

            var responses = new List<JsonRpcResponse>();
            foreach (var item in batch)
            {
                var outcome = await DispatchTokenAsync(item, cancellationToken);
                if (outcome.Response is JsonRpcResponse response)
                    responses.Add(response);
            }

            return responses.Count == 0
                ? new McpDispatchOutcome(McpDispatchKind.NoResponse)
                : new McpDispatchOutcome(McpDispatchKind.Response, responses);
        }

        return await DispatchTokenAsync(token, cancellationToken);
    }

    private async Task<McpDispatchOutcome> DispatchTokenAsync(
        JToken token,
        CancellationToken cancellationToken)
    {
        if (token is not JObject envelope)
            return Response(Error(null, -32600, "Invalid Request"));

        var idToken = envelope["id"];
        object? id = idToken?.Type == JTokenType.Null ? null : idToken?.ToObject<object>();
        var isNotification = idToken == null;

        if (envelope["jsonrpc"]?.Type != JTokenType.String
            || envelope["jsonrpc"]?.ToString() != "2.0"
            || envelope["method"]?.Type != JTokenType.String
            || string.IsNullOrWhiteSpace(envelope["method"]?.ToString())
            || idToken?.Type == JTokenType.Null
            || (idToken != null && idToken.Type is not (JTokenType.String or JTokenType.Integer or JTokenType.Float))
            || (envelope["params"] != null && envelope["params"] is not (JObject or JArray)))
        {
            return Response(Error(id, -32600, "Invalid Request"));
        }

        var method = envelope["method"]!.ToString();
        var parameters = envelope["params"] as JObject;

        try
        {
            object? result;
            switch (method)
            {
                case "initialize":
                    if (parameters == null)
                        return Response(Error(id, -32602, "Invalid params"));

                    var requestedVersion = parameters["protocolVersion"]?.ToString();
                    if (requestedVersion != SupportedProtocolVersion)
                    {
                        return Response(Error(id, -32602, "Unsupported protocol version", new
                        {
                            supportedVersions = new[] { SupportedProtocolVersion }
                        }));
                    }

                    result = new
                    {
                        protocolVersion = SupportedProtocolVersion,
                        capabilities = new 
                        { 
                            tools = new { listChanged = false },
                            prompts = new { listChanged = false },
                            resources = new { listChanged = false }
                        },
                        serverInfo = new { name = "FlowOS MCP Server", version = "1.1.0" },
                        instructions = FlowOsMcpGuidance.SystemInstructions
                    };
                    break;

                case "notifications/initialized":
                    return new McpDispatchOutcome(McpDispatchKind.NoResponse);

                case "ping":
                    result = new JObject();
                    break;

                case "tools/list":
                    result = new { tools = _toolRegistry.GetTools() };
                    break;

                case "tools/call":
                    if (parameters == null)
                        return Response(Error(id, -32602, "Invalid params"));

                    var callParams = parameters.ToObject<CallToolParams>();
                    if (callParams == null || string.IsNullOrWhiteSpace(callParams.Name))
                        return Response(Error(id, -32602, "Invalid params"));
                    if (!_toolRegistry.Contains(callParams.Name))
                        return Response(Error(id, -32602, $"Unknown tool: {callParams.Name}"));

                    result = await _toolRegistry.ExecuteAsync(
                        callParams.Name,
                        callParams.Arguments ?? new JObject());
                    break;

                case "prompts/list":
                    result = FlowOsMcpGuidance.GetPromptsList();
                    break;

                case "prompts/get":
                    if (parameters == null || string.IsNullOrWhiteSpace(parameters["name"]?.ToString()))
                        return Response(Error(id, -32602, "name is required for prompts/get"));

                    var promptName = parameters["name"]!.ToString();
                    var promptArgs = parameters["arguments"] as JObject;
                    var promptResult = FlowOsMcpGuidance.GetPrompt(promptName, promptArgs);
                    if (promptResult == null)
                        return Response(Error(id, -32602, $"Prompt not found: {promptName}"));

                    result = promptResult;
                    break;

                case "resources/list":
                    result = FlowOsMcpGuidance.GetResourcesList();
                    break;

                case "resources/read":
                    if (parameters == null || string.IsNullOrWhiteSpace(parameters["uri"]?.ToString()))
                        return Response(Error(id, -32602, "uri is required for resources/read"));

                    var resourceUri = parameters["uri"]!.ToString();
                    var resourceResult = FlowOsMcpGuidance.GetResource(resourceUri);
                    if (resourceResult == null)
                        return Response(Error(id, -32602, $"Resource not found: {resourceUri}"));

                    result = resourceResult;
                    break;

                default:
                    return isNotification
                        ? new McpDispatchOutcome(McpDispatchKind.NoResponse)
                        : Response(Error(id, -32601, "Method not found"));
            }

            return isNotification
                ? new McpDispatchOutcome(McpDispatchKind.NoResponse)
                : Response(new JsonRpcResponse { Id = id, Result = result });
        }
        catch (Exception)
        {
            return isNotification
                ? new McpDispatchOutcome(McpDispatchKind.NoResponse)
                : Response(Error(id, -32603, "Internal error"));
        }
    }

    private static McpDispatchOutcome Response(JsonRpcResponse response) =>
        new(McpDispatchKind.Response, response);

    private static JsonRpcResponse Error(object? id, int code, string message, object? data = null) =>
        new()
        {
            Id = id,
            Error = new JsonRpcError
            {
                Code = code,
                Message = message,
                Data = data
            }
        };
    }
