using System.Diagnostics;
using System.Text.Json;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations.Adapters;
using FlowOS.Domain.Entities;

namespace FlowOS.Agents.Implementations;

/// <summary>
/// Hosted LLM workflow agent supporting OpenAI, Anthropic, and Google via provider adapters.
/// The API key is loaded internally and never copied onto Agent Context.
/// </summary>
public sealed class TenantLlmWorkflowAgent : IConversationalAgent
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
    private readonly IAgentPromptAuditor? _auditor;

    public TenantLlmWorkflowAgent(
        string providerName,
        string? model,
        string? endpoint,
        string? apiKey,
        HttpMessageHandler? handler = null,
        ILlmProviderAdapter? adapter = null,
        ILlmTransport? transport = null,
        IAgentPromptAuditor? auditor = null)
    {
        _providerName = string.IsNullOrWhiteSpace(providerName) ? "openai" : providerName.Trim();
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model.Trim();
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? string.Empty : endpoint.Trim();
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _customClient = handler != null ? new HttpClient(handler, disposeHandler: false) : null;
        _adapter = adapter ?? new FlowOS.Agents.Implementations.Adapters.LlmProviderAdapterRegistry(new FlowOS.Agents.Implementations.Adapters.ILlmProviderAdapterFactory[] {
            new FlowOS.Agents.Implementations.Adapters.OpenAiAdapterFactory(),
            new FlowOS.Agents.Implementations.Adapters.AzureOpenAiAdapterFactory(),
            new FlowOS.Agents.Implementations.Adapters.CustomOpenAiAdapterFactory(),
            new FlowOS.Agents.Implementations.Adapters.AnthropicAdapterFactory(),
            new FlowOS.Agents.Implementations.Adapters.GoogleAdapterFactory()
        }).GetAdapter(_providerName);
        _transport = transport;
        _auditor = auditor;
    }

    public int MaxTurns => 5;

    public Task<AgentResult> ExecuteAsync(AgentContext context) =>
        ExecuteAsync(context, CancellationToken.None);

    public Task<AgentResult> ExecuteConversationalAsync(
        DecisionPacket packet,
        IReadOnlyList<ChatMessage> history,
        IAgentToolInvoker? toolInvoker = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInternalAsync(AgentContext.FromPacket(packet, toolInvoker: toolInvoker), history, cancellationToken);
    }

    public Task<AgentResult> ExecuteAsync(
        AgentContext context,
        CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(context, Array.Empty<ChatMessage>(), cancellationToken);
    }

    private async Task<AgentResult> ExecuteInternalAsync(
        AgentContext context,
        IReadOnlyList<ChatMessage> history,
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
        var toolCallHistory = new List<ToolCallMessage>();
        var iterations = 0;
        const int MaxIterations = 10;
        AgentTelemetry? lastTelemetry = null;

        try
        {
            while (iterations < MaxIterations)
            {
                var combinedHistory = new List<ToolCallMessage>();
                foreach (var msg in history)
                {
                    combinedHistory.Add(new ToolCallMessage(
                        msg.Role.ToString().ToLowerInvariant(),
                        msg.Content,
                        Name: msg.Name));
                }
                combinedHistory.AddRange(toolCallHistory);

                // Always include the workflow packet as the initial user message.
                // The tool-call exchanges for this run live in toolCallHistory; repeating
                // the packet on every iteration keeps the model grounded in the context.
                var iterationUserPrompt = userPrompt;
                var toolNames = context.Packet?.DeclaredTools?.Select(t => t.Name).ToList();

                using var request = _adapter.CreateRequest(
                    _endpoint, 
                    _apiKey, 
                    _model, 
                    systemPrompt, 
                    iterationUserPrompt, 
                    context.Packet?.DeclaredTools, 
                    combinedHistory.Count > 0 ? combinedHistory : null);

                var sw = Stopwatch.StartNew();
                var response = _transport == null
                    ? await SendDirectAsync(request, cancellationToken)
                    : await _transport.SendAsync(request, cancellationToken);
                sw.Stop();
                var durationMs = sw.ElapsedMilliseconds;

                var transportTelemetry = new AgentTelemetry(
                    response.HttpStatusCode,
                    response.ProviderRequestId,
                    AttemptCount: Math.Max(0, response.AttemptCount));

                if (_auditor != null && _auditor.IsEnabled)
                {
                    var providerResponseForAudit = response.Success && !string.IsNullOrWhiteSpace(response.Content)
                        ? _adapter.ParseResponse(response.Content!)
                        : null;

                    var rawRequestPayload = JsonSerializer.Serialize(new
                    {
                        SystemPrompt = systemPrompt,
                        UserPrompt = iterationUserPrompt,
                        TurnMessages = combinedHistory,
                        ToolNames = toolNames
                    });

                    var rawResponsePayload = response.Content ?? (providerResponseForAudit?.ErrorCode != null
                        ? "error:" + providerResponseForAudit.ErrorCode
                        : string.Empty);

                    var auditRecord = new AgentPromptAuditRecord(
                        auditId: Guid.NewGuid(),
                        tenantId: context.TenantId,
                        providerName: _providerName,
                        model: _model,
                        recordedAtUtc: DateTime.UtcNow,
                        rawRequestPayload: rawRequestPayload,
                        durationMs: durationMs,
                        iteration: iterations,
                        workflowInstanceId: context.Packet?.WorkflowInstanceId,
                        stepId: context.Packet?.CurrentStepId,
                        providerAlias: context.Packet?.Provider?.Alias,
                        systemPrompt: systemPrompt,
                        userPrompt: iterationUserPrompt,
                        rawResponsePayload: rawResponsePayload,
                        failureCode: response.FailureCode ?? providerResponseForAudit?.ErrorCode,
                        httpStatusCode: response.HttpStatusCode,
                        inputTokens: providerResponseForAudit?.InputTokens,
                        outputTokens: providerResponseForAudit?.OutputTokens,
                        executionId: null,
                        correlationId: null);

                    try { await _auditor.TryRecordAsync(auditRecord, cancellationToken); } catch { }
                }
                
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
                    toolCallHistory.Add(new ToolCallMessage("assistant", providerResponse.Content, providerResponse.ToolCalls));

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
                        toolCallHistory.Add(new ToolCallMessage("tool", tcResult, ToolCallId: tc.Id, ToolName: tc.ToolName));
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
                if (parsed.StructuredOutput != null)
                {
                    result.StructuredData = parsed.StructuredOutput;
                }
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
            ?? "You are a FlowOS workflow agent. Suggest one or more legal nextSteps events as JSON.";
        var toolInfo = packet?.DeclaredTools is { Count: > 0 } tools
            ? $"\nAvailable tools: {string.Join(", ", tools.Select(t => t.Name))}"
            : string.Empty;
        return $"{system}\nLegal events: {string.Join(", ", legal)}{toolInfo}\nReply with JSON: insight (string), actions (array of objects with eventType, confidence, reason), and optionally structuredOutput (a JSON object of business data extracted or computed for this step, e.g. amounts or line items - omit it if there is nothing to report).";
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
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!TryReadBoundedString(root, "insight", 2000, out var insight))
                return false;

            var actions = new List<SuggestedAction>();

            if (root.TryGetProperty("actions", out var actionsProp) && actionsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var actionElement in actionsProp.EnumerateArray())
                {
                    if (actionElement.ValueKind != JsonValueKind.Object ||
                        !TryReadBoundedString(actionElement, "eventType", 200, out var eventType) ||
                        !TryReadBoundedString(actionElement, "reason", 2000, out var reason) ||
                        !actionElement.TryGetProperty("confidence", out var confidenceElement) ||
                        confidenceElement.ValueKind != JsonValueKind.Number ||
                        !confidenceElement.TryGetDouble(out var confidence) ||
                        !double.IsFinite(confidence) ||
                        confidence is < 0 or > 1)
                    {
                        return false;
                    }
                    actions.Add(new SuggestedAction(eventType, reason, confidence));
                }
            }
            else
            {
                // Fallback to legacy single-action format
                if (!TryReadBoundedString(root, "eventType", 200, out var eventType) ||
                    !TryReadBoundedString(root, "reason", 2000, out var reason) ||
                    !root.TryGetProperty("confidence", out var confidenceElement) ||
                    confidenceElement.ValueKind != JsonValueKind.Number ||
                    !confidenceElement.TryGetDouble(out var confidence) ||
                    !double.IsFinite(confidence) ||
                    confidence is < 0 or > 1)
                {
                    return false;
                }
                actions.Add(new SuggestedAction(eventType, reason, confidence));
            }

            if (actions.Count == 0)
                return false;

            parsed = new ParsedSuggestion(insight, actions, TryReadStructuredOutput(root));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Tenant integration finding N4: an optional top-level "structuredOutput" field alongside
    /// insight/actions, carrying business data the agent extracted or computed for this step
    /// (amounts, line items, risk scores, etc.) into AgentResult.StructuredData. This is best-effort -
    /// a missing, malformed, or wrongly-typed structuredOutput never fails the suggestion; it is
    /// simply left out, the same way a provider that doesn't support it (or a model that omits it)
    /// always behaved before this field existed.
    /// Schema-constrained providers (OpenAI strict JSON schema, Google responseSchema) carry this as a
    /// JSON-encoded string rather than a nested object, since their schemas require a closed shape -
    /// so this also accepts a string containing JSON, parsing it the same way.
    /// </summary>
    private static Dictionary<string, object>? TryReadStructuredOutput(JsonElement root)
    {
        if (!root.TryGetProperty("structuredOutput", out var structuredOutputProp))
            return null;

        var candidate = structuredOutputProp;
        if (candidate.ValueKind == JsonValueKind.String)
        {
            var raw = candidate.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            try
            {
                using var parsedString = JsonDocument.Parse(raw);
                candidate = parsedString.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        if (candidate.ValueKind != JsonValueKind.Object)
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(candidate.GetRawText());
        }
        catch (JsonException)
        {
            return null;
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

    private sealed record ParsedSuggestion(string Insight, List<SuggestedAction> Actions, Dictionary<string, object>? StructuredOutput = null);
}
