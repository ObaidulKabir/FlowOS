using System.Text.Json;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations.Adapters;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Domain.Entities.ExternalAI;
using Microsoft.Extensions.Logging;
using FlowOS.MCP.Models;

namespace FlowOS.MCP.Services;

public sealed class DefaultExternalAIAgentPlanner : IExternalAIAgentPlanner
{
    private readonly IExternalAgentChangeStore _changeStore;
    private readonly IExternalAgentPlanStore _planStore;
    private readonly IPluginBindingRegistryService _bindings;
    private readonly ILlmProviderAdapterRegistry _adapterRegistry;
    private readonly ILlmTransport _transport;
    private readonly IToolRegistry _toolRegistry;
    private readonly IFlowOsHostedLlmRuntime? _hosted;
    private readonly ILogger<DefaultExternalAIAgentPlanner> _logger;

    public DefaultExternalAIAgentPlanner(
        IExternalAgentChangeStore changeStore,
        IExternalAgentPlanStore planStore,
        IPluginBindingRegistryService bindings,
        ILlmProviderAdapterRegistry adapterRegistry,
        ILlmTransport transport,
        IToolRegistry toolRegistry,
        ILogger<DefaultExternalAIAgentPlanner> logger,
        IFlowOsHostedLlmRuntime? hosted = null)
    {
        _changeStore = changeStore;
        _planStore = planStore;
        _bindings = bindings;
        _adapterRegistry = adapterRegistry;
        _transport = transport;
        _toolRegistry = toolRegistry;
        _logger = logger;
        _hosted = hosted;
    }

    public async Task<ExternalAIAgentPlanResult> PlanAsync(
        Guid changeId,
        Guid? requestedTenantId = null,
        string? agentProfileId = null,
        string? objective = null,
        CancellationToken cancellationToken = default)
    {
        var change = await _changeStore.GetByIdAsync(changeId, cancellationToken)
            ?? throw new InvalidOperationException($"Change '{changeId}' was not found.");

        if (requestedTenantId.HasValue && requestedTenantId.Value != change.TenantId)
            throw new InvalidOperationException("Requested tenant does not own the change.");

        var toolSpecs = BuildAllowedToolSpecs();
        var provider = await ResolveProviderAsync(change.TenantId, agentProfileId, cancellationToken);

        string responseText;
        AgentTelemetry? telemetry = null;
        var success = false;

        try
        {
            responseText = await RequestPlanAsync(change, provider, toolSpecs, objective, false, cancellationToken);
            if (!TryParsePlan(responseText, out var rawSteps))
            {
                responseText = await RequestPlanAsync(change, provider, toolSpecs, objective, true, cancellationToken);
                if (!TryParsePlan(responseText, out rawSteps))
                    rawSteps = Array.Empty<ParsedPlanStep>();
            }

            var dropped = new List<string>();
            var validated = ValidateSteps(rawSteps, toolSpecs, change, dropped);
            if (validated.Count == 0)
            {
                validated.Add(new ExternalAIAgentPlannedStep(
                    "step-inspect-change",
                    0,
                    "get_external_agent_change",
                    JsonSerializer.Serialize(new { changeId = change.Id }),
                    "Inspect the change details before any manual or automated follow-up.",
                    Array.Empty<string>()));
            }

            var serializedPlan = JsonSerializer.Serialize(validated.Select(step => new
            {
                step.StepId,
                step.StepIndex,
                step.ToolName,
                toolArgs = JsonDocument.Parse(step.ToolArgsJson).RootElement,
                step.Description,
                dependsOn = step.DependsOn
            }));

            var plan = ExternalAgentPlanRecord.Create(
                Guid.NewGuid(),
                change.Id,
                change.TenantId,
                string.IsNullOrWhiteSpace(agentProfileId) ? "default" : agentProfileId.Trim(),
                DateTime.UtcNow,
                serializedPlan,
                1);

            var stepRecords = validated
                .Select(step => ExternalAgentPlanStepRecord.Create(
                    0,
                    plan.Id,
                    step.StepId,
                    step.StepIndex,
                    step.ToolName,
                    step.ToolArgsJson,
                    step.Description,
                    JsonSerializer.Serialize(step.DependsOn)))
                .ToList();

            await _planStore.CreatePlanAsync(plan, stepRecords, cancellationToken);
            success = true;

            return new ExternalAIAgentPlanResult(
                plan.Id,
                change.Id,
                change.TenantId,
                string.IsNullOrWhiteSpace(agentProfileId) ? null : agentProfileId.Trim(),
                dropped,
                validated,
                serializedPlan);
        }
        finally
        {
            await FinalizeHostedLeaseAsync(provider, success, telemetry, cancellationToken);
        }
    }

    private async Task<string> RequestPlanAsync(
        ExternalAgentChangeRecord change,
        ResolvedPlannerProvider provider,
        IReadOnlyDictionary<string, ToolSpec> toolSpecs,
        string? objective,
        bool strictRetry,
        CancellationToken cancellationToken)
    {
        var adapter = _adapterRegistry.GetAdapter(provider.ProviderName, provider.Configuration);
        var toolList = string.Join("\n", toolSpecs.Values.Select(tool =>
            $"- {tool.Name}: {tool.Description} (sideEffect={tool.SideEffect})"));

        var systemPrompt = $"""
You are FlowOS External AI Planner.
Return only a JSON array.
Each item must be an object with:
- stepId: string
- toolName: string
- toolArgs: object
- description: string
- dependsOn: string[]

Rules:
- Use only tool names from the allowed tool list.
- Include tenantId in toolArgs when the tool is tenant-scoped.
- Keep steps sequential and dependency-aware.
- Do not include markdown fences.
- Prefer 1 to 5 steps.

Allowed tools:
{toolList}
""";

        if (strictRetry)
            systemPrompt += "\nYour previous reply was invalid. Respond with JSON only. No prose.";

        var payloadToken = TryParsePayload(change.PayloadJson);
        var userPrompt = JsonSerializer.Serialize(new
        {
            objective = string.IsNullOrWhiteSpace(objective)
                ? "Review this change and create the smallest valid MCP execution plan."
                : objective.Trim(),
            change = new
            {
                change.Id,
                change.TenantId,
                change.EventType,
                change.SourceOutboxMessageId,
                change.CreatedAtUtc,
                payload = payloadToken
            }
        });

        using var request = adapter.CreateRequest(
            provider.Endpoint ?? string.Empty,
            provider.ApiKey,
            provider.Model,
            systemPrompt,
            userPrompt);

        var response = await _transport.SendAsync(request, cancellationToken);
        if (!response.Success)
            throw new InvalidOperationException(response.FailureCode ?? "Planner provider call failed.");

        var rawContent = response.Content ?? string.Empty;
        var parsed = adapter.ParseResponse(rawContent);
        if (parsed.ErrorCode != null)
            throw new InvalidOperationException(parsed.ErrorCode);

        if (!string.IsNullOrWhiteSpace(parsed.Content))
            return parsed.Content;

        // Some transports/tests return the model text directly instead of a provider envelope.
        return rawContent;
    }

    private List<ExternalAIAgentPlannedStep> ValidateSteps(
        IReadOnlyList<ParsedPlanStep> rawSteps,
        IReadOnlyDictionary<string, ToolSpec> toolSpecs,
        ExternalAgentChangeRecord change,
        List<string> dropped)
    {
        var result = new List<ExternalAIAgentPlannedStep>();
        var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < rawSteps.Count; index++)
        {
            var step = rawSteps[index];
            if (!_toolRegistry.Contains(step.ToolName) || !toolSpecs.ContainsKey(step.ToolName))
            {
                dropped.Add(step.ToolName);
                continue;
            }

            var stepId = string.IsNullOrWhiteSpace(step.StepId)
                ? $"step-{index + 1}"
                : step.StepId.Trim();
            if (!stepIds.Add(stepId))
            {
                stepId = $"step-{index + 1}";
                stepIds.Add(stepId);
            }

            var args = step.ToolArgs.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(step.ToolArgs.GetRawText()) ?? new Dictionary<string, object?>()
                : new Dictionary<string, object?>();

            if (!args.ContainsKey("tenantId"))
                args["tenantId"] = change.TenantId;

            result.Add(new ExternalAIAgentPlannedStep(
                stepId,
                result.Count,
                step.ToolName,
                JsonSerializer.Serialize(args),
                string.IsNullOrWhiteSpace(step.Description) ? null : step.Description.Trim(),
                step.DependsOn
                    .Where(dep => !string.IsNullOrWhiteSpace(dep))
                    .Select(dep => dep.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()));
        }

        return result;
    }

    private IReadOnlyDictionary<string, ToolSpec> BuildAllowedToolSpecs()
    {
        var allowedPrefixes = new[]
        {
            "get_agent_context",
            "preview_agent_context",
            "suggest_agent_action",
            "run_agent_task",
            "start_workflow",
            "publish_event",
            "complete_task",
            "list_external_agent_changes",
            "get_external_agent_change",
            "get_external_agent_plan"
        };

        return _toolRegistry.GetTools()
            .Where(tool => allowedPrefixes.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(
                tool => tool.Name,
                tool => new ToolSpec(
                    tool.Name,
                    tool.Description,
                    McpToolDescriptions.ProfileFor(tool.Name).SideEffect),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<ResolvedPlannerProvider> ResolveProviderAsync(
        Guid tenantId,
        string? agentProfileId,
        CancellationToken cancellationToken)
    {
        AgentProfilePublicSettings? profile = null;
        if (!string.IsNullOrWhiteSpace(agentProfileId))
        {
            var profileBinding = await _bindings.GetAgentProfileAsync(tenantId, agentProfileId.Trim(), cancellationToken);
            profile = profileBinding?.Configuration as AgentProfilePublicSettings;
        }

        var providerAlias = profile?.ProviderAlias;
        if (!string.IsNullOrWhiteSpace(providerAlias) &&
            string.Equals(providerAlias, AgentProviderKinds.FlowosHosted, StringComparison.OrdinalIgnoreCase))
        {
            return await LeaseHostedAsync(tenantId, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(providerAlias))
        {
            var binding = await _bindings.GetEnabledAsync(tenantId, PluginBindingTypes.Agent, providerAlias.Trim(), cancellationToken);
            if (binding != null)
            {
                var secrets = await _bindings.GetAgentSecretsAsync(tenantId, binding.SourceName, cancellationToken);
                if (!string.IsNullOrWhiteSpace(secrets?.ApiKey))
                {
                    return new ResolvedPlannerProvider(
                        binding.SourceName,
                        binding.ProviderName,
                        secrets.Model ?? "gpt-4o-mini",
                        secrets.Endpoint,
                        secrets.ApiKey,
                        secrets,
                        null);
                }
            }
        }

        var defaultBinding = await _bindings.GetDefaultAgentProviderAsync(tenantId, cancellationToken);
        if (defaultBinding != null)
        {
            var secrets = await _bindings.GetAgentSecretsAsync(tenantId, defaultBinding.SourceName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(secrets?.ApiKey))
            {
                return new ResolvedPlannerProvider(
                    defaultBinding.SourceName,
                    defaultBinding.ProviderName,
                    secrets.Model ?? "gpt-4o-mini",
                    secrets.Endpoint,
                    secrets.ApiKey,
                    secrets,
                    null);
            }
        }

        if (_hosted is { IsConfigured: true })
            return await LeaseHostedAsync(tenantId, cancellationToken);

        throw new InvalidOperationException("No planner-capable agent provider is configured for this tenant.");
    }

    private async Task<ResolvedPlannerProvider> LeaseHostedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (_hosted == null)
            throw new InvalidOperationException(FlowOsHostedLlmCodes.UnavailableMessage);

        var lease = await _hosted.TryLeaseAsync(tenantId, cancellationToken);
        if (!lease.Allowed || string.IsNullOrWhiteSpace(lease.ApiKey))
            throw new InvalidOperationException(lease.Message ?? FlowOsHostedLlmCodes.UnavailableMessage);

        return new ResolvedPlannerProvider(
            AgentProviderKinds.FlowosHosted,
            AgentProviderKinds.OpenAi,
            string.IsNullOrWhiteSpace(lease.Model) ? "gpt-4o-mini" : lease.Model,
            lease.Endpoint,
            lease.ApiKey,
            null,
            lease);
    }

    private async Task FinalizeHostedLeaseAsync(
        ResolvedPlannerProvider provider,
        bool succeeded,
        AgentTelemetry? telemetry,
        CancellationToken cancellationToken)
    {
        if (_hosted == null || provider.HostedLease == null)
            return;

        try
        {
            await _hosted.FinalizeAsync(
                provider.HostedLease,
                succeeded,
                telemetry?.InputTokens ?? 0,
                telemetry?.OutputTokens ?? 0,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to finalize hosted planner lease for tenant {TenantId}.", provider.HostedLease.TenantId);
        }
    }

    private static object? TryParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<object>(payloadJson);
        }
        catch
        {
            return payloadJson;
        }
    }

    private static bool TryParsePlan(string text, out IReadOnlyList<ParsedPlanStep> steps)
    {
        steps = Array.Empty<ParsedPlanStep>();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(StripCodeFence(text));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            var parsed = new List<ParsedPlanStep>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var toolName = item.TryGetProperty("toolName", out var toolNameProp) && toolNameProp.ValueKind == JsonValueKind.String
                    ? toolNameProp.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(toolName))
                    continue;

                var stepId = item.TryGetProperty("stepId", out var stepIdProp) && stepIdProp.ValueKind == JsonValueKind.String
                    ? stepIdProp.GetString()
                    : null;
                var description = item.TryGetProperty("description", out var descriptionProp) && descriptionProp.ValueKind == JsonValueKind.String
                    ? descriptionProp.GetString()
                    : null;

                JsonElement toolArgs = default;
                if (item.TryGetProperty("toolArgs", out var toolArgsProp))
                    toolArgs = toolArgsProp.Clone();

                var dependsOn = new List<string>();
                if (item.TryGetProperty("dependsOn", out var dependsOnProp) && dependsOnProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dep in dependsOnProp.EnumerateArray())
                    {
                        if (dep.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(dep.GetString()))
                            dependsOn.Add(dep.GetString()!.Trim());
                    }
                }

                parsed.Add(new ParsedPlanStep(
                    stepId,
                    toolName.Trim(),
                    toolArgs.ValueKind == JsonValueKind.Undefined ? JsonDocument.Parse("{}").RootElement.Clone() : toolArgs,
                    description,
                    dependsOn));
            }

            steps = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string StripCodeFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var withoutOpen = trimmed[3..];
        var newlineIndex = withoutOpen.IndexOfAny(new[] { '\r', '\n' });
        if (newlineIndex >= 0)
            withoutOpen = withoutOpen[(newlineIndex + 1)..];

        var closingIndex = withoutOpen.LastIndexOf("```", StringComparison.Ordinal);
        return closingIndex >= 0
            ? withoutOpen[..closingIndex].Trim()
            : withoutOpen.Trim();
    }

    private sealed record ToolSpec(string Name, string Description, string SideEffect);

    private sealed record ParsedPlanStep(
        string? StepId,
        string ToolName,
        JsonElement ToolArgs,
        string? Description,
        IReadOnlyList<string> DependsOn);

    private sealed record ResolvedPlannerProvider(
        string ProviderAlias,
        string ProviderName,
        string Model,
        string? Endpoint,
        string? ApiKey,
        AgentProviderConfiguration? Configuration,
        FlowOsHostedLlmLease? HostedLease);
}
