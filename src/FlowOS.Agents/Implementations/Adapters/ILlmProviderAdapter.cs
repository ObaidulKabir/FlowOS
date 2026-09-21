using System.Net.Http;

namespace FlowOS.Agents.Implementations.Adapters;

public sealed record LlmProviderResponse(
    string? Content,
    long? InputTokens = null,
    long? OutputTokens = null,
    long? TotalTokens = null,
    string? ProviderRequestId = null);

public interface ILlmProviderAdapter
{
    HttpRequestMessage CreateRequest(
        string endpoint,
        string? apiKey,
        string model,
        string systemPrompt,
        string userPrompt);

    string? ExtractContent(string responseBody);

    LlmProviderResponse ParseResponse(string responseBody) =>
        new(ExtractContent(responseBody));
}
