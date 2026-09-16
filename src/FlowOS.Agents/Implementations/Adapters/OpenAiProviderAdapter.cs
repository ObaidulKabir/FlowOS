using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FlowOS.Agents.Implementations.Adapters;

public sealed class OpenAiProviderAdapter : ILlmProviderAdapter
{
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
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var payload = new
        {
            model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
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
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString();
                }
            }

            return root.TryGetProperty("content", out var direct) ? direct.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
