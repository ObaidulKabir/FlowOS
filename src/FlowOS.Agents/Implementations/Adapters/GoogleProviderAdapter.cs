using System.Net.Http;
using System.Text;
using System.Text.Json;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Agents.Implementations.Adapters;

public sealed class GoogleProviderAdapter : ILlmProviderAdapter
{
    public HttpRequestMessage CreateRequest(
        string endpoint,
        string? apiKey,
        string model,
        string systemPrompt,
        string userPrompt)
    {
        var targetModel = string.IsNullOrWhiteSpace(model) ? "gemini-1.5-flash" : model;
        var url = string.IsNullOrWhiteSpace(endpoint)
            ? $"https://generativelanguage.googleapis.com/v1beta/models/{targetModel}:generateContent"
            : endpoint.Trim();

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

        var payload = new
        {
            system_instruction = new
            {
                parts = new object[] { new { text = systemPrompt } }
            },
            contents = new object[]
            {
                new
                {
                    role = "user",
                    parts = new object[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
                response_mime_type = "application/json"
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
            string? contentText = null;
            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array &&
                candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.ValueKind == JsonValueKind.Array &&
                    parts.GetArrayLength() > 0)
                {
                    if (parts[0].TryGetProperty("text", out var textProp) &&
                        textProp.ValueKind == JsonValueKind.String)
                    {
                        contentText = textProp.GetString();
                    }
                }
            }

            long? inputTokens = null;
            long? outputTokens = null;
            long? totalTokens = null;
            if (root.TryGetProperty("usageMetadata", out var usage) &&
                usage.ValueKind == JsonValueKind.Object)
            {
                inputTokens = ReadNonNegativeInt64(usage, "promptTokenCount");
                outputTokens = ReadNonNegativeInt64(usage, "candidatesTokenCount");
                totalTokens = ReadNonNegativeInt64(usage, "totalTokenCount");
            }

            var requestId = root.TryGetProperty("responseId", out var id) &&
                            id.ValueKind == JsonValueKind.String
                ? LlmTelemetrySanitizer.SanitizeRequestId(id.GetString())
                : null;

            return new LlmProviderResponse(
                contentText,
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
