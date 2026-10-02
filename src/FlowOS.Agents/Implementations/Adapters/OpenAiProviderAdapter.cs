using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Agents.Implementations.Adapters;

public sealed class OpenAiProviderAdapter : ILlmProviderAdapter
{
    private readonly bool _useStrictJsonSchema;
    private readonly bool _useApiKeyHeader;

    public OpenAiProviderAdapter(
        bool useStrictJsonSchema = true,
        bool useApiKeyHeader = false)
    {
        _useStrictJsonSchema = useStrictJsonSchema;
        _useApiKeyHeader = useApiKeyHeader;
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
            ? "https://api.openai.com/v1/chat/completions"
            : endpoint.Trim();

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (_useApiKeyHeader)
                request.Headers.TryAddWithoutValidation("api-key", apiKey);
            else
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        object responseFormat = SupportsStrictJsonSchema(model)
            ? new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "flowos_agent_suggestion",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "eventType", "confidence", "reason", "insight" },
                        properties = new
                        {
                            eventType = new { type = "string", minLength = 1, maxLength = 200 },
                            confidence = new { type = "number", minimum = 0, maximum = 1 },
                            reason = new { type = "string", minLength = 1, maxLength = 2000 },
                            insight = new { type = "string", minLength = 1, maxLength = 2000 }
                        }
                    }
                }
            }
            : new { type = "json_object" };

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };

        if (history != null)
        {
            foreach (var msg in history)
            {
                if (msg.Role == "tool")
                {
                    messages.Add(new
                    {
                        role = "tool",
                        content = msg.Content ?? "",
                        tool_call_id = msg.ToolCallId
                    });
                }
                else
                {
                    if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        messages.Add(new
                        {
                            role = "assistant",
                            content = msg.Content,
                            tool_calls = msg.ToolCalls.Select(tc => new
                            {
                                id = tc.Id,
                                type = "function",
                                function = new
                                {
                                    name = tc.ToolName,
                                    arguments = tc.ArgumentsJson
                                }
                            }).ToArray()
                        });
                    }
                    else
                    {
                        messages.Add(new { role = "assistant", content = msg.Content ?? "" });
                    }
                }
            }
        }

        object? toolsPayload = null;
        if (tools != null && tools.Count > 0)
        {
            toolsPayload = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = string.IsNullOrWhiteSpace(t.ParametersSchema) 
                        ? new { type = "object", properties = new { } } 
                        : JsonSerializer.Deserialize<object>(t.ParametersSchema)
                }
            }).ToArray();
        }

        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages
        };

        if (toolsPayload != null)
        {
            payload["tools"] = toolsPayload;
            payload["tool_choice"] = "auto";
        }
        else
        {
            payload["response_format"] = responseFormat;
        }

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }

    private bool SupportsStrictJsonSchema(string model)
    {
        if (!_useStrictJsonSchema || string.IsNullOrWhiteSpace(model))
            return false;

        var normalized = model.Trim().ToLowerInvariant();
        return normalized.StartsWith("gpt-4o", StringComparison.Ordinal) ||
               normalized.StartsWith("gpt-4.1", StringComparison.Ordinal) ||
               normalized.StartsWith("gpt-5", StringComparison.Ordinal) ||
               normalized.StartsWith("o1", StringComparison.Ordinal) ||
               normalized.StartsWith("o3", StringComparison.Ordinal) ||
               normalized.StartsWith("o4", StringComparison.Ordinal);
    }

    public string? ExtractContent(string responseBody) =>
        ParseResponse(responseBody).Content;

    public LlmProviderResponse ParseResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            
            string? content = null;
            List<ToolCallInfo>? toolCalls = null;
            
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("content", out var contentElement) &&
                        contentElement.ValueKind == JsonValueKind.String)
                    {
                        content = contentElement.GetString();
                    }
                    
                    if (message.TryGetProperty("tool_calls", out var tcElement) &&
                        tcElement.ValueKind == JsonValueKind.Array)
                    {
                        toolCalls = new List<ToolCallInfo>();
                        foreach (var tc in tcElement.EnumerateArray())
                        {
                            if (tc.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "function" &&
                                tc.TryGetProperty("id", out var idEl) &&
                                tc.TryGetProperty("function", out var funcEl) &&
                                funcEl.TryGetProperty("name", out var nameEl) &&
                                funcEl.TryGetProperty("arguments", out var argsEl))
                            {
                                toolCalls.Add(new ToolCallInfo(
                                    idEl.GetString() ?? "",
                                    nameEl.GetString() ?? "",
                                    argsEl.GetString() ?? "{}"));
                            }
                        }
                    }
                }
            }

            if (content == null &&
                root.TryGetProperty("content", out var direct) &&
                direct.ValueKind == JsonValueKind.String)
            {
                content = direct.GetString();
            }

            long? inputTokens = null, outputTokens = null, totalTokens = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt64(out var pv)) inputTokens = pv;
                if (usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt64(out var cv)) outputTokens = cv;
                if (usage.TryGetProperty("total_tokens", out var t) && t.TryGetInt64(out var tv)) totalTokens = tv;
            }

            string? requestId = null;
            if (root.TryGetProperty("id", out var idElement))
            {
                requestId = idElement.GetString();
            }

            return new LlmProviderResponse(
                content,
                inputTokens,
                outputTokens,
                totalTokens,
                requestId,
                null,
                toolCalls != null && toolCalls.Count > 0 ? toolCalls : null);
        }
        catch
        {
            return new LlmProviderResponse(null);
        }
    }
}
