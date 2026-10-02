using System.Net.Http;
using System.Text;
using System.Text.Json;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Agents.Implementations.Adapters;

public sealed class AnthropicProviderAdapter : ILlmProviderAdapter
{
    private readonly int _maxTokens;

    /// <param name="maxTokens">Maximum output tokens. Defaults to 1024. Set higher for verbose reasoning models.</param>
    public AnthropicProviderAdapter(int maxTokens = 1024)
    {
        _maxTokens = maxTokens > 0 ? maxTokens : 1024;
    }

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
            max_tokens = _maxTokens,
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
            if (errorCode == null && root.TryGetProperty("content", out var contentArr) &&
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
                requestId,
                errorCode);
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
