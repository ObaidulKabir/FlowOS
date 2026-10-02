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
    /// <summary>
    /// Shared pooled HttpClient for all direct (non-transport) provider calls.
    /// HttpClient is thread-safe and designed to be reused across requests.
    /// </summary>
    private static readonly HttpClient SharedClient = new();

    private readonly string _providerName;
    private readonly string _model;
    private readonly string _endpoint;
    private readonly string? _apiKey;
    private readonly HttpClient? _customClient;
    private readonly ILlmProviderAdapter _adapter;
    private readonly ILlmTransport? _transport;

    public TenantLlmWorkflowAgent(
        string providerName,
        string? model,
        string? endpoint,
        string? apiKey,
        HttpMessageHandler? handler = null,
        ILlmProviderAdapter? adapter = null,
        ILlmTransport? transport = null)
    {
        _providerName = string.IsNullOrWhiteSpace(providerName) ? "openai" : providerName.Trim();
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model.Trim();
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? string.Empty : endpoint.Trim();
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        // Only allocate a custom client when a test/override handler is provided.
        _customClient = handler != null ? new HttpClient(handler, disposeHandler: false) : null;
        _adapter = adapter ?? LlmProviderAdapterFactory.GetAdapter(_providerName);
        _transport = transport;
    }

    public Task<AgentResult> ExecuteAsync(AgentContext context) =>
        ExecuteAsync(context, CancellationToken.None);

    public async Task<AgentResult> ExecuteAsync(
        AgentContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return AgentResult.Failure(
                AgentFailureCodes.ProviderAuth,
                "Provider API key is not configured.");
        }

        var systemPrompt = BuildSystemPrompt(context);
        var userPrompt = BuildUserPrompt(context);
        var history = new List<ToolCallMessage>();
        var iterations = 0;
        const int MaxIterations = 10;
        AgentTelemetry? lastTelemetry = null;

        try
        {
            while (iterations < MaxIterations)
            {
                using var request = _adapter.CreateRequest(
                    _endpoint, 
                    _apiKey, 
                    _model, 
                    systemPrompt, 
                    iterations == 0 ? userPrompt : "", 
                    context.Packet?.DeclaredTools, 
                    history.Count > 0 ? history : null);

                var response = _transport == null
                    ? await SendDirectAsync(request, cancellationToken)
                    : await _transport.SendAsync(request, cancellationToken);

                var transportTelemetry = new AgentTelemetry(
                    response.HttpStatusCode,
                    response.ProviderRequestId,
                    AttemptCount: Math.Max(0, response.AttemptCount));
                
                if (!response.Success)
                {
                    return AgentResult.Failure(
                        response.FailureCode ?? AgentFailureCodes.ProviderUnavailable,
                        FailureMessage(response.FailureCode),
                        transportTelemetry);
                }

                var providerResponse = _adapter.ParseResponse(response.Content ?? string.Empty);
                var telemetry = new AgentTelemetry(
                    response.HttpStatusCode,
                    response.ProviderRequestId ?? providerResponse.ProviderRequestId,
                    providerResponse.InputTokens,
                    providerResponse.OutputTokens,
                    providerResponse.TotalTokens,
                    Math.Max(0, response.AttemptCount));
                
                lastTelemetry = telemetry;

                if (providerResponse.ErrorCode != null)
                {
                    return AgentResult.Failure(
                        providerResponse.ErrorCode,
                        $"Provider returned a specific error code: {providerResponse.ErrorCode}",
                        telemetry);
                }

                if (providerResponse.ToolCalls != null && providerResponse.ToolCalls.Count > 0)
                {
                    history.Add(new ToolCallMessage("assistant", providerResponse.Content, providerResponse.ToolCalls));

                    if (context.ToolInvoker == null)
                    {
                        return AgentResult.Failure(
                            AgentFailureCodes.ProviderConfiguration,
                            "Provider requested tool invocation, but no IAgentToolInvoker is available in the context.",
                            telemetry);
                    }

                    foreach (var tc in providerResponse.ToolCalls)
                    {
                        string tcResult;
                        try
                        {
                            tcResult = await context.ToolInvoker.InvokeToolAsync(tc.ToolName, tc.ArgumentsJson, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            tcResult = $"{{\"ok\":false,\"error\":\"{ex.Message}\"}}";
                        }
                        history.Add(new ToolCallMessage("tool", tcResult, ToolCallId: tc.Id, ToolName: tc.ToolName));
                    }

                    iterations++;
                    continue;
                }

                if (!TryParseSuggestion(providerResponse.Content, out var parsed))
                {
                    return AgentResult.Failure(
                        AgentFailureCodes.InvalidModelOutput,
                        "Provider returned model output that did not match the required suggestion contract.",
                        telemetry);
                }

                var result = AgentResult.WithActions(parsed.Insight, parsed.Actions);
                result.Telemetry = telemetry;
                if (context.RestrictToLegalEvents)
                {
                    result = AgentSuggestionContract.RestrictToLegalEvents(result, context.LegalEvents);
                    if (result.SuggestedActions.Count == 0)
                    {
                        return AgentResult.Failure(
                            AgentFailureCodes.InvalidModelOutput,
                            "Provider suggested an event outside the legal nextSteps set.",
                            telemetry);
                    }
                }

                return result;
            }
            
            return AgentResult.Failure(
                AgentFailureCodes.InvalidModelOutput,
                $"Provider exceeded maximum tool loop iterations ({MaxIterations}).",
                lastTelemetry);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AgentResult.Failure(
                AgentFailureCodes.ProviderUnavailable,
                "Provider call failed.",
                lastTelemetry);
        }
    }

    private static string BuildSystemPrompt(AgentContext context)
    {
        var packet = context.Packet;
        var legal = packet?.LegalNextStepEvents ?? context.LegalEvents;
        var system = packet?.Prompt.System
            ?? "You are a FlowOS workflow agent. Suggest one legal nextSteps event as JSON.";
        var toolInfo = packet?.DeclaredTools is { Count: > 0 } tools
            ? $"\nAvailable tools: {string.Join(", ", tools.Select(t => t.Name))}"
            : string.Empty;
        return $"{system}\nLegal events: {string.Join(", ", legal)}{toolInfo}\nReply with JSON: eventType, confidence, reason, insight.";
    }

    private static string BuildUserPrompt(AgentContext context)
    {
        return TokenBudget.BuildBudgetedPrompt(context);
    }

    private async Task<LlmTransportResult> SendDirectAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _customClient ?? SharedClient;
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var statusCode = (int)response.StatusCode;
            var requestId = ReadProviderRequestId(response);
            if (!response.IsSuccessStatusCode)
            {
                return new LlmTransportResult(
                    false,
                    null,
                    statusCode,
                    requestId,
                    FailureCode(statusCode));
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new LlmTransportResult(
                true,
                body,
                statusCode,
                requestId,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return new LlmTransportResult(
                false, null, null, null, AgentFailureCodes.ProviderTimeout);
        }
        catch (HttpRequestException)
        {
            return new LlmTransportResult(
                false, null, null, null, AgentFailureCodes.ProviderUnavailable);
        }
    }

    private static bool TryParseSuggestion(
        string? content,
        out ParsedSuggestion parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            using var suggestion = JsonDocument.Parse(StripMarkdownCodeFence(content));
            var root = suggestion.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryReadBoundedString(root, "eventType", 200, out var eventType) ||
                !TryReadBoundedString(root, "reason", 2000, out var reason) ||
                !TryReadBoundedString(root, "insight", 2000, out var insight) ||
                !root.TryGetProperty("confidence", out var confidenceElement) ||
                confidenceElement.ValueKind != JsonValueKind.Number ||
                !confidenceElement.TryGetDouble(out var confidence) ||
                !double.IsFinite(confidence) ||
                confidence is < 0 or > 1)
            {
                return false;
            }

            parsed = new ParsedSuggestion(
                insight,
                [new SuggestedAction(eventType, reason, confidence)]);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Strips markdown code fences that some LLMs (Anthropic, Google) wrap around JSON output.
    /// Handles: ```json ... ```, ``` ... ```, and leading/trailing whitespace.
    /// </summary>
    internal static string StripMarkdownCodeFence(string content)
    {
        var trimmed = content.AsSpan().Trim();

        // Must start with ``` to be a code fence
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return content.Trim();

        // Strip opening fence (```json, ```JSON, ```)
        var afterFence = trimmed[3..];
        var newline = afterFence.IndexOfAny(['\n', '\r']);
        if (newline >= 0)
            afterFence = afterFence[(newline + 1)..];
        else
            afterFence = afterFence.TrimStart();

        // Strip closing fence
        var closingFence = afterFence.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFence >= 0)
            afterFence = afterFence[..closingFence];

        return afterFence.Trim().ToString();
    }

    private static bool TryReadBoundedString(
        JsonElement element,
        string name,
        int maximumLength,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = property.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > maximumLength)
            return false;

        value = candidate;
        return true;
    }

    private static string? ReadProviderRequestId(HttpResponseMessage response)
    {
        foreach (var name in new[] { "x-request-id", "request-id", "openai-request-id" })
        {
            if (!response.Headers.TryGetValues(name, out var values))
                continue;

            var sanitized = LlmTelemetrySanitizer.SanitizeRequestId(values.FirstOrDefault());
            if (sanitized != null)
                return sanitized;
        }

        return null;
    }

    private static string FailureCode(int statusCode) =>
        statusCode switch
        {
            401 or 403 => AgentFailureCodes.ProviderAuth,
            429 => AgentFailureCodes.ProviderRateLimit,
            408 => AgentFailureCodes.ProviderTimeout,
            _ => AgentFailureCodes.ProviderUnavailable
        };

    private static string FailureMessage(string? failureCode) =>
        failureCode switch
        {
            AgentFailureCodes.ProviderAuth => "Provider authentication failed.",
            AgentFailureCodes.ProviderRateLimit => "Provider rate limit was reached.",
            AgentFailureCodes.ProviderTimeout => "Provider request timed out.",
            _ => "Provider is unavailable."
        };

    private sealed record ParsedSuggestion(string Insight, List<SuggestedAction> Actions);
}
