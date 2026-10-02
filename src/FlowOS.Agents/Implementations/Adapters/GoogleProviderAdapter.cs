using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Agents.Implementations.Adapters;

public sealed class GoogleProviderAdapter : ILlmProviderAdapter
{
    private readonly bool _useSystemInstruction;
    private readonly bool _useResponseSchema;

    public GoogleProviderAdapter(
        bool useSystemInstruction = true,
        bool useResponseSchema = true)
    {
        _useSystemInstruction = useSystemInstruction;
        _useResponseSchema = useResponseSchema;
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
        var modelName = string.IsNullOrWhiteSpace(model) ? "gemini-1.5-pro" : model.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(endpoint)
            ? $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent"
            : endpoint.Trim();

        var url = string.IsNullOrWhiteSpace(apiKey)
            ? baseUrl
            : $"{baseUrl}?key={apiKey}";

        var request = new HttpRequestMessage(HttpMethod.Post, url);

        var contents = new List<object>();

        if (history != null)
        {
            foreach (var msg in history)
            {
                if (msg.Role == "tool")
                {
                    contents.Add(new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new
                            {
                                functionResponse = new
                                {
                                    name = msg.ToolName,
                                    response = new { result = msg.Content ?? "" }
                                }
                            }
                        }
                    });
                }
                else
                {
                    var parts = new List<object>();
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                    {
                        parts.Add(new { text = msg.Content });
                    }
                    if (msg.ToolCalls != null)
                    {
                        foreach (var tc in msg.ToolCalls)
                        {
                            parts.Add(new
                            {
                                functionCall = new
                                {
                                    name = tc.ToolName,
                                    args = JsonSerializer.Deserialize<object>(tc.ArgumentsJson)
                                }
                            });
                        }
                    }
                    if (parts.Count > 0)
                    {
                        contents.Add(new { role = msg.Role == "assistant" ? "model" : "user", parts = parts.ToArray() });
                    }
                }
            }
        }
        else
        {
            contents.Add(new
            {
                role = "user",
                parts = new[] { new { text = userPrompt } }
            });
        }

        var payload = new Dictionary<string, object>
        {
            ["contents"] = contents
        };

        if (_useSystemInstruction && !string.IsNullOrWhiteSpace(systemPrompt))
        {
            payload["systemInstruction"] = new
            {
                parts = new[] { new { text = systemPrompt } }
            };
        }
        else if (!_useSystemInstruction && !string.IsNullOrWhiteSpace(systemPrompt))
        {
            if (contents.Count > 0 && contents[0] is Dictionary<string, object> dict && dict.ContainsKey("parts") && dict["parts"] is object[] parts)
            {
                var newParts = new List<object> { new { text = systemPrompt } };
                newParts.AddRange(parts);
                dict["parts"] = newParts.ToArray();
            }
        }

        if (tools != null && tools.Count > 0)
        {
            payload["tools"] = new[]
            {
                new
                {
                    functionDeclarations = tools.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = string.IsNullOrWhiteSpace(t.ParametersSchema)
                            ? new { type = "OBJECT", properties = new { } }
                            : JsonSerializer.Deserialize<object>(t.ParametersSchema)
                    }).ToArray()
                }
            };
        }
        else if (_useResponseSchema)
        {
            payload["generationConfig"] = new
            {
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "object",
                    required = new[] { "eventType", "confidence", "reason", "insight" },
                    properties = new
                    {
                        eventType = new { type = "string" },
                        confidence = new { type = "number" },
                        reason = new { type = "string" },
                        insight = new { type = "string" }
                    }
                }
            };
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
            
            string? content = null;
            List<ToolCallInfo>? toolCalls = null;
            string? errorCode = null;

            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array &&
                candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];

                if (first.TryGetProperty("finishReason", out var frElement) && 
                    frElement.ValueKind == JsonValueKind.String)
                {
                    var finishReason = frElement.GetString();
                    if (finishReason == "SAFETY" || finishReason == "RECITATION")
                    {
                        errorCode = AgentFailureCodes.InvalidModelOutput;
                    }
                }

                if (errorCode == null && first.TryGetProperty("content", out var cElement))
                {
                    if (cElement.TryGetProperty("parts", out var parts) &&
                        parts.ValueKind == JsonValueKind.Array)
                    {
                        toolCalls = new List<ToolCallInfo>();
                        var sb = new StringBuilder();
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textProp) &&
                                textProp.ValueKind == JsonValueKind.String)
                            {
                                sb.Append(textProp.GetString());
                            }
                            else if (part.TryGetProperty("functionCall", out var fcProp))
                            {
                                if (fcProp.TryGetProperty("name", out var nameProp) &&
                                    fcProp.TryGetProperty("args", out var argsProp))
                                {
                                    toolCalls.Add(new ToolCallInfo(
                                        Guid.NewGuid().ToString(), // Gemini doesn't provide IDs
                                        nameProp.GetString() ?? "",
                                        JsonSerializer.Serialize(argsProp)));
                                }
                            }
                        }
                        if (sb.Length > 0)
                        {
                            content = sb.ToString();
                        }
                    }
                }
            }

            long? inputTokens = null, outputTokens = null, totalTokens = null;
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var p) && p.TryGetInt64(out var pv)) inputTokens = pv;
                if (usage.TryGetProperty("candidatesTokenCount", out var c) && c.TryGetInt64(out var cv)) outputTokens = cv;
                if (usage.TryGetProperty("totalTokenCount", out var t) && t.TryGetInt64(out var tv)) totalTokens = tv;
            }

            return new LlmProviderResponse(
                content,
                inputTokens,
                outputTokens,
                totalTokens,
                null,
                errorCode,
                toolCalls != null && toolCalls.Count > 0 ? toolCalls : null);
        }
        catch
        {
            return new LlmProviderResponse(null);
        }
    }
}
