using System.Net.Http;
using System.Text;
using System.Text.Json;

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

        if (!string.IsNullOrWhiteSpace(apiKey) && !url.Contains("key="))
        {
            var separator = url.Contains('?') ? "&" : "?";
            url = $"{url}{separator}key={apiKey}";
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url);

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

    public string? ExtractContent(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
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
                    return parts[0].TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
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
