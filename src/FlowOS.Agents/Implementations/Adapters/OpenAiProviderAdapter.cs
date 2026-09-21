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
        string userPrompt)
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

        var payload = new
        {
            model,
            response_format = responseFormat,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

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
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElement) &&
                    contentElement.ValueKind == JsonValueKind.String)
                {
                    content = contentElement.GetString();
                }
            }

            if (content == null &&
                root.TryGetProperty("content", out var direct) &&
                direct.ValueKind == JsonValueKind.String)
            {
                content = direct.GetString();
            }

            long? inputTokens = null;
            long? outputTokens = null;
            long? totalTokens = null;
            if (root.TryGetProperty("usage", out var usage) &&
                usage.ValueKind == JsonValueKind.Object)
            {
                inputTokens = ReadNonNegativeInt64(usage, "prompt_tokens");
                outputTokens = ReadNonNegativeInt64(usage, "completion_tokens");
                totalTokens = ReadNonNegativeInt64(usage, "total_tokens");
            }

            var requestId = root.TryGetProperty("id", out var id) &&
                            id.ValueKind == JsonValueKind.String
                ? LlmTelemetrySanitizer.SanitizeRequestId(id.GetString())
                : null;

            return new LlmProviderResponse(
                content,
                inputTokens,
                outputTokens,
                totalTokens ?? SumTokens(inputTokens, outputTokens),
                requestId);
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
