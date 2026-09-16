using System.Net.Http;
using System.Text;
using System.Text.Json;

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

    public string? ExtractContent(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("content", out var contentArr) &&
                contentArr.ValueKind == JsonValueKind.Array &&
                contentArr.GetArrayLength() > 0)
            {
                foreach (var block in contentArr.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var textProp))
                        return textProp.GetString();
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
