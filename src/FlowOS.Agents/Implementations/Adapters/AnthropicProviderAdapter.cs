using System.Net.Http;
using System.Text;
using System.Text.Json;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Agents.Implementations.Adapters;

public sealed class AnthropicProviderAdapter : ILlmProviderAdapter
{
    public HttpRequestMessage CreateRequest(
        string endpoint,
        string? apiKey,
        string model,
        string systemPrompt,
        string userPrompt)
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

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(model) ? "claude-3-5-sonnet-20241022" : model,
            max_tokens = 1024,
            system = systemPrompt,
            messages = new object[]
            {
                new { role = "user", content = userPrompt }
            }
        };

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
            if (root.TryGetProperty("content", out var contentArr) &&
                contentArr.ValueKind == JsonValueKind.Array &&
                contentArr.GetArrayLength() > 0)
            {
                foreach (var block in contentArr.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var textProp) &&
                        textProp.ValueKind == JsonValueKind.String)
                    {
                        content = textProp.GetString();
                        break;
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
