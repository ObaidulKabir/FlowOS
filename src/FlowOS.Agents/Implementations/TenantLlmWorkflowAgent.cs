using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Agents.Implementations;

/// <summary>
/// Hosted OpenAI-compatible chat client. The API key is loaded internally and never copied onto Agent Context.
/// </summary>
public sealed class TenantLlmWorkflowAgent : IWorkflowAgent
{
    private readonly string _providerName;
    private readonly string? _model;
    private readonly string _endpoint;
    private readonly string? _apiKey;
    private readonly HttpMessageHandler? _handler;

    public TenantLlmWorkflowAgent(
        string providerName,
        string? model,
        string? endpoint,
        string? apiKey,
        HttpMessageHandler? handler = null)
    {
        _providerName = string.IsNullOrWhiteSpace(providerName) ? "openai" : providerName.Trim();
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model.Trim();
        _endpoint = string.IsNullOrWhiteSpace(endpoint)
            ? "https://api.openai.com/v1/chat/completions"
            : endpoint.Trim();
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _handler = handler;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return AgentResult.Failure($"Agent provider '{_providerName}' is missing an API key.");

        try
        {
            using var client = _handler == null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(BuildRequestBody(context), Encoding.UTF8, "application/json");

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

    private string BuildRequestBody(AgentContext context)
    {
        var packet = context.Packet;
        var legal = packet?.LegalNextStepEvents ?? context.LegalEvents;
        var system = packet?.Prompt.System
            ?? "You are a FlowOS workflow agent. Suggest one legal nextSteps event as JSON.";
        var instructions = packet?.Prompt.Instructions
            ?? packet?.Objective
            ?? context.Objective;
        var payload = new
        {
            model = _model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = $"{system}\nLegal events: {string.Join(", ", legal)}\nReply with JSON: eventType, confidence, reason, insight."
                },
                new
                {
                    role = "user",
                    content = JsonSerializer.Serialize(new
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
                    })
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static ParsedSuggestion? ParseSuggestion(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var content = ExtractContent(root);
        if (string.IsNullOrWhiteSpace(content))
            return null;

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

    private static string? ExtractContent(JsonElement root)
    {
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

        return root.TryGetProperty("content", out var direct) ? direct.GetString() : root.GetRawText();
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) ? prop.GetString() : null;

    private sealed record ParsedSuggestion(string Insight, List<SuggestedAction> Actions);
}
