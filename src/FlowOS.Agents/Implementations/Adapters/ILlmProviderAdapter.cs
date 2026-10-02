using System.Collections.Generic;
using System.Net.Http;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Agents.Implementations.Adapters;

public sealed record ToolCallInfo(
    string Id,
    string ToolName,
    string ArgumentsJson);

public sealed record ToolCallMessage(
    string Role, // "assistant", "tool"
    string? Content,
    IReadOnlyList<ToolCallInfo>? ToolCalls = null,
    string? ToolCallId = null,
    string? ToolName = null);

public sealed record LlmProviderResponse(
    string? Content,
    long? InputTokens = null,
    long? OutputTokens = null,
    long? TotalTokens = null,
    string? ProviderRequestId = null,
    string? ErrorCode = null,
    IReadOnlyList<ToolCallInfo>? ToolCalls = null);

public interface ILlmProviderAdapter
{
    HttpRequestMessage CreateRequest(
        string endpoint,
        string? apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<AgentToolDescriptor>? tools = null,
        IReadOnlyList<ToolCallMessage>? history = null);

    string? ExtractContent(string responseBody);

    LlmProviderResponse ParseResponse(string responseBody) =>
        new(ExtractContent(responseBody));
}
