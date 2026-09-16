using System.Net.Http;

namespace FlowOS.Agents.Implementations.Adapters;

public interface ILlmProviderAdapter
{
    HttpRequestMessage CreateRequest(
        string endpoint,
        string? apiKey,
        string model,
        string systemPrompt,
        string userPrompt);

    string? ExtractContent(string responseBody);
}
