using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Agents.Implementations.Adapters;

public sealed class AnthropicProviderAdapter : ILlmProviderAdapter
{
    private readonly int _maxTokens;

    public AnthropicProviderAdapter(int maxTokens = 1024)
    {
        _maxTokens = maxTokens > 0 ? maxTokens : 1024;
    }

    public HttpRequestMessage CreateRequest(
        string endpoint,
        string? apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<AgentToolDescriptor>? tools = null,
        IReadOnlyList<ToolCallMessage>? history = null)
    {
        var url = string.IsNullOrWhiteSpace(endpoint)
            ? "https://api.anthropic.com/v1/messages"
            : endpoint.Trim();

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        }
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        var messages = new List<object>();

        if (history != null)
        {
            foreach (var msg in history)
            {
                if (msg.Role == "tool")
                {
                    messages.Add(new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "tool_result",
                                tool_use_id = msg.ToolCallId,
                                content = msg.Content ?? ""
                            }
                        }
                    });
                }
                else
                {
                    var contents = new List<object>();
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                    {
                        contents.Add(new { type = "text", text = msg.Content });
                    }
                    if (msg.ToolCalls != null)
                    {
                        foreach (var tc in msg.ToolCalls)
                        {
                            contents.Add(new
                            {
                                type = "tool_use",
                                id = tc.Id,
                                name = tc.ToolName,
                                input = JsonSerializer.Deserialize<object>(tc.ArgumentsJson)
                            });
                        }
                    }
                    
                    if (contents.Count > 0)
                    {
                        messages.Add(new { role = msg.Role, content = contents.ToArray() });
                    }
                }
            }
        }
        else
        {
            messages.Add(new { role = "user", content = userPrompt });
        }

        var payload = new Dictionary<string, object>
        {
            ["model"] = string.IsNullOrWhiteSpace(model) ? "claude-3-5-sonnet-20241022" : model,
            ["max_tokens"] = _maxTokens,
            ["system"] = systemPrompt,
            ["messages"] = messages
        };

        if (tools != null && tools.Count > 0)
        {
            payload["tools"] = tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                input_schema = string.IsNullOrWhiteSpace(t.ParametersSchema)
                    ? new { type = "object", properties = new { } }
                    : JsonSerializer.Deserialize<object>(t.ParametersSchema)
            }).ToArray();
        }

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }

    public string? ExtractContent(string responseBody) =>
        ParseResponse(responseBody).Content;

    public LlmProviderResponse ParseResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            
            string? errorCode = null;
            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "error" &&
                root.TryGetProperty("error", out var errObj) &&
                errObj.TryGetProperty("type", out var errType))
            {
                var errTypeStr = errType.GetString();
                if (errTypeStr == "overloaded_error")
                {
                    errorCode = AgentFailureCodes.ProviderUnavailable;
                }
                else if (errTypeStr == "rate_limit_error")
                {
                    errorCode = AgentFailureCodes.ProviderRateLimit;
                }
            }

            string? content = null;
            List<ToolCallInfo>? toolCalls = null;
            
            if (errorCode == null && root.TryGetProperty("content", out var contentArr) &&
                contentArr.ValueKind == JsonValueKind.Array &&
                contentArr.GetArrayLength() > 0)
            {
                toolCalls = new List<ToolCallInfo>();
                foreach (var block in contentArr.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var blockType))
                    {
                        var type = blockType.GetString();
                        if (type == "text" && block.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                        {
                            content = textProp.GetString();
                        }
                        else if (type == "tool_use" &&
                                 block.TryGetProperty("id", out var idProp) &&
                                 block.TryGetProperty("name", out var nameProp) &&
                                 block.TryGetProperty("input", out var inputProp))
                        {
                            toolCalls.Add(new ToolCallInfo(
                                idProp.GetString() ?? "",
                                nameProp.GetString() ?? "",
                                JsonSerializer.Serialize(inputProp)));
                        }
                    }
                }
            }

            long? inputTokens = null;
            long? outputTokens = null;
            if (root.TryGetProperty("usage", out var usage) &&
                usage.ValueKind == JsonValueKind.Object)
            {
                inputTokens = ReadNonNegativeInt64(usage, "input_tokens");
                outputTokens = ReadNonNegativeInt64(usage, "output_tokens");
            }

            var requestId = root.TryGetProperty("id", out var id) &&
                            id.ValueKind == JsonValueKind.String
                ? LlmTelemetrySanitizer.SanitizeRequestId(id.GetString())
                : null;

            return new LlmProviderResponse(
                content,
                inputTokens,
                outputTokens,
                SumTokens(inputTokens, outputTokens),
                requestId,
                errorCode,
                toolCalls != null && toolCalls.Count > 0 ? toolCalls : null);
        }
        catch
        {
            return new LlmProviderResponse(null);
        }
    }

    private static long? ReadNonNegativeInt64(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var parsed) &&
        parsed >= 0
            ? parsed
            : null;

    private static long? SumTokens(long? inputTokens, long? outputTokens) =>
        inputTokens.HasValue &&
        outputTokens.HasValue &&
        inputTokens.Value <= long.MaxValue - outputTokens.Value
            ? inputTokens.Value + outputTokens.Value
            : null;
}
