using System.Text.Json;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations.Adapters;

namespace FlowOS.Agents.Implementations;

/// <summary>
/// Hosted LLM workflow agent supporting OpenAI, Anthropic, and Google via provider adapters.
/// The API key is loaded internally and never copied onto Agent Context.
/// </summary>
public sealed class TenantLlmWorkflowAgent : IWorkflowAgent
{
    private readonly string _providerName;
    private readonly string _model;
    private readonly string _endpoint;
    private readonly string? _apiKey;
    private readonly HttpMessageHandler? _handler;
    private readonly ILlmProviderAdapter _adapter;

    public TenantLlmWorkflowAgent(
        string providerName,
        string? model,
        string? endpoint,
        string? apiKey,
        HttpMessageHandler? handler = null,
        ILlmProviderAdapter? adapter = null)
    {
        _providerName = string.IsNullOrWhiteSpace(providerName) ? "openai" : providerName.Trim();
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model.Trim();
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? string.Empty : endpoint.Trim();
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _handler = handler;
        _adapter = adapter ?? LlmProviderAdapterFactory.GetAdapter(_providerName);
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return AgentResult.Failure($"Agent provider '{_providerName}' is missing an API key.");

        try
        {
            using var client = _handler == null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
            var systemPrompt = BuildSystemPrompt(context);
            var userPrompt = BuildUserPrompt(context);
            using var request = _adapter.CreateRequest(_endpoint, _apiKey, _model, systemPrompt, userPrompt);

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return AgentResult.Failure($"Agent provider '{_providerName}' returned {(int)response.StatusCode}.");

            var parsed = ParseSuggestion(body);
            if (parsed == null)
                return AgentResult.Failure($"Agent provider '{_providerName}' returned an unreadable suggestion.");

            var result = AgentResult.WithActions(parsed.Insight, parsed.Actions);
            if (context.RestrictToLegalEvents)
                result = AgentSuggestionContract.RestrictToLegalEvents(result, context.LegalEvents);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AgentResult.Failure($"Agent provider '{_providerName}' call failed.");
        }
    }

    private static string BuildSystemPrompt(AgentContext context)
    {
        var packet = context.Packet;
        var legal = packet?.LegalNextStepEvents ?? context.LegalEvents;
        var system = packet?.Prompt.System
            ?? "You are a FlowOS workflow agent. Suggest one legal nextSteps event as JSON.";
        return $"{system}\nLegal events: {string.Join(", ", legal)}\nReply with JSON: eventType, confidence, reason, insight.";
    }

    private static string BuildUserPrompt(AgentContext context)
    {
        var packet = context.Packet;
        var instructions = packet?.Prompt.Instructions
            ?? packet?.Objective
            ?? context.Objective;
        return JsonSerializer.Serialize(new
        {
            instructions,
            templateGuideline = packet?.Prompt.TemplateGuideline,
            policyGuideline = packet?.Prompt.PolicyGuideline,
            currentStepId = packet?.CurrentStepId,
            currentState = packet?.CurrentState,
            data = packet?.CanonicalContext,
            eventPayloads = packet?.EventPayloads,
            toolResults = packet?.ToolResults,
            snapshot = context.EntitySnapshot
        });
    }

    private ParsedSuggestion? ParseSuggestion(string body)
    {
        var content = _adapter.ExtractContent(body);
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            using var suggestion = JsonDocument.Parse(content);
            var s = suggestion.RootElement;
            var eventType = ReadString(s, "eventType") ?? ReadString(s, "event");
            var reason = ReadString(s, "reason") ?? "Hosted agent suggestion.";
            var insight = ReadString(s, "insight") ?? reason;
            var confidence = 0.5;
            if (s.TryGetProperty("confidence", out var confProp) && confProp.TryGetDouble(out var conf))
                confidence = conf;

            var actions = new List<SuggestedAction>();
            if (!string.IsNullOrWhiteSpace(eventType))
                actions.Add(new SuggestedAction(eventType, reason, confidence));

            return new ParsedSuggestion(insight, actions);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) ? prop.GetString() : null;

    private sealed record ParsedSuggestion(string Insight, List<SuggestedAction> Actions);
}
