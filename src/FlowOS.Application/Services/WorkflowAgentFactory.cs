using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;

namespace FlowOS.Application.Services;

public sealed class WorkflowAgentFactory : IWorkflowAgentFactory
{
    private readonly IPluginBindingRegistryService _bindings;
    private readonly ILlmProviderAdapterRegistry _adapterRegistry;
    private readonly HttpMessageHandler? _handler;
    private readonly IFlowOsHostedLlmRuntime? _hosted;
    private readonly ILlmTransport? _transport;
    private readonly IAgentPromptAuditor? _auditor;

    public WorkflowAgentFactory(
        IPluginBindingRegistryService bindings,
        ILlmProviderAdapterRegistry adapterRegistry,
        HttpMessageHandler? handler = null,
        IFlowOsHostedLlmRuntime? hosted = null,
        ILlmTransport? transport = null,
        IAgentPromptAuditor? auditor = null)
    {
        _bindings = bindings;
        _adapterRegistry = adapterRegistry;
        _handler = handler;
        _hosted = hosted;
        _transport = transport;
        _auditor = auditor;
    }

    public async Task<IWorkflowAgent> CreateAsync(
        DecisionPacket packet,
        string? requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(packet, requestedAgentId, cancellationToken);
        return resolved.UsageReservation == null
            ? resolved.Agent
            : new UsageFinalizingWorkflowAgent(resolved.Agent, resolved.UsageReservation);
    }

    public async Task<ResolvedWorkflowAgent> ResolveAsync(
        DecisionPacket packet,
        string? requestedAgentId = null,
        CancellationToken cancellationToken = default)
    {
        return await ResolveChainAsync(packet, requestedAgentId, new HashSet<string>(StringComparer.OrdinalIgnoreCase), cancellationToken);
    }

    private async Task<ResolvedWorkflowAgent> ResolveChainAsync(
        DecisionPacket packet,
        string? requestedAgentId,
        HashSet<string> visitedAliases,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var primary = await ResolveSingleAsync(packet, requestedAgentId, cancellationToken);
        
        string? fallbackAlias = null;
        if (AgentProviderKinds.IsByoLlm(primary.ProviderName) && 
            !string.IsNullOrWhiteSpace(primary.ProviderAlias) &&
            !AgentProviderKinds.IsFlowOsHosted(primary.ProviderAlias))
        {
            try
            {
                var secrets = await _bindings.GetAgentSecretsAsync(packet.TenantId, primary.ProviderAlias, cancellationToken);
                fallbackAlias = secrets?.FallbackProviderAlias;
            }
            catch
            {
                // Ignore if secrets are missing here; ResolveSingleAsync handled exceptions for the primary agent already
            }
        }

        if (string.IsNullOrWhiteSpace(fallbackAlias) || !visitedAliases.Add(fallbackAlias))
        {
            return primary;
        }

        var fallbackBinding = await _bindings.GetEnabledAsync(packet.TenantId, PluginBindingTypes.Agent, fallbackAlias, cancellationToken);
        if (fallbackBinding == null)
        {
            return primary;
        }

        var fallbackProviderRef = new AgentProviderRef(
            fallbackBinding.SourceName,
            fallbackBinding.ProviderName,
            packet.Provider?.Model,
            null,
            false);
            
        var fallbackPacket = packet with { Provider = fallbackProviderRef };
        var fallbackResolved = await ResolveChainAsync(fallbackPacket, requestedAgentId, visitedAliases, cancellationToken);

        var wrappedAgent = new FallbackWorkflowAgent(primary.Agent, new[] { fallbackResolved.Agent });
        return primary with { Agent = wrappedAgent };
    }

    private async Task<ResolvedWorkflowAgent> ResolveSingleAsync(
        DecisionPacket packet,
        string? requestedAgentId = null,
        CancellationToken cancellationToken = default)
    {
        var providerName = packet.Provider?.ProviderName;
        if (IsExplicitFlowOsRisk(providerName))
            return RiskDescriptor();

        AgentProviderConfiguration? secrets = null;
        if (AgentProviderKinds.IsByoLlm(providerName) &&
            !string.IsNullOrWhiteSpace(packet.Provider?.Alias))
        {
            try
            {
                secrets = await _bindings.GetAgentSecretsAsync(
                    packet.TenantId,
                    packet.Provider.Alias,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                throw new InvalidOperationException(
                    "Agent provider configuration is unavailable.");
            }
        }

        if (ShouldUseFlowOsHosted(providerName))
        {
            if (_hosted == null)
            {
                throw new InvalidOperationException(
                    $"{FlowOsHostedLlmCodes.Unavailable}: {FlowOsHostedLlmCodes.UnavailableMessage}");
            }

            FlowOsHostedLlmLease lease;
            try
            {
                lease = await _hosted.TryLeaseAsync(packet.TenantId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                throw new InvalidOperationException(
                    $"{FlowOsHostedLlmCodes.Unavailable}: Hosted provider reservation failed.");
            }
            if (!lease.Allowed)
            {
                throw new InvalidOperationException(
                    $"{lease.Code ?? FlowOsHostedLlmCodes.Unavailable}: {lease.Message ?? FlowOsHostedLlmCodes.UnavailableMessage}");
            }

            var model = NormalizeModel(AgentProviderKinds.OpenAi, lease.Model);
            var adapter = _adapterRegistry.GetAdapter(AgentProviderKinds.OpenAi);
            var enableAudit = false;
            var agent = new TenantLlmWorkflowAgent(
                AgentProviderKinds.OpenAi,
                model,
                lease.Endpoint,
                lease.ApiKey,
                _handler,
                adapter: adapter,
                transport: _transport,
                auditor: enableAudit ? _auditor : null);
            return new ResolvedWorkflowAgent(
                agent,
                ResolveLlmActorId(requestedAgentId, AgentProviderKinds.FlowosHosted),
                nameof(TenantLlmWorkflowAgent),
                AgentProviderKinds.FlowosHosted,
                AgentProviderKinds.OpenAi,
                model,
                new HostedUsageReservation(_hosted, lease));
        }

        if (AgentProviderKinds.IsByoLlm(providerName))
        {
            var alias = string.IsNullOrWhiteSpace(packet.Provider?.Alias)
                ? providerName!.Trim()
                : packet.Provider.Alias.Trim();
            var model = NormalizeModel(
                providerName!,
                secrets?.Model ?? packet.Provider?.Model);

            var adapter = _adapterRegistry.GetAdapter(providerName, secrets);

            var enableAudit = secrets?.EnablePromptAudit ?? false;
            var agent = new TenantLlmWorkflowAgent(
                providerName!,
                model,
                secrets?.Endpoint ?? packet.Provider?.Endpoint,
                secrets?.ApiKey,
                _handler,
                adapter: adapter,
                transport: _transport,
                auditor: enableAudit ? _auditor : null);
            return new ResolvedWorkflowAgent(
                agent,
                ResolveLlmActorId(requestedAgentId, alias),
                nameof(TenantLlmWorkflowAgent),
                alias,
                providerName!.Trim().ToLowerInvariant(),
                model);
        }

        if (IsRiskIdentity(requestedAgentId))
        {
            return RiskDescriptor();
        }

        throw new InvalidOperationException(
            $"No hosted agent is registered for '{requestedAgentId}' (provider '{providerName}').");
    }

    private bool ShouldUseFlowOsHosted(string? providerName)
    {
        if (AgentProviderKinds.IsFlowOsHosted(providerName))
            return true;

        return string.IsNullOrWhiteSpace(providerName) && _hosted is { IsConfigured: true };
    }

    private static bool IsExplicitFlowOsRisk(string? providerName) =>
        string.Equals(providerName, AgentProviderKinds.FlowosRisk, StringComparison.OrdinalIgnoreCase);

    private static ResolvedWorkflowAgent RiskDescriptor() =>
        new(
            new RiskAnalysisAgent(),
            AgentProviderKinds.FlowosRisk,
            nameof(RiskAnalysisAgent),
            AgentProviderKinds.FlowosRisk,
            AgentProviderKinds.FlowosRisk,
            null);

    private static string ResolveLlmActorId(
        string? requestedAgentId,
        string providerActorId)
    {
        if (IsRiskIdentity(requestedAgentId))
            return providerActorId;

        return requestedAgentId!.Trim();
    }

    private static bool IsRiskIdentity(string? agentId) =>
        string.IsNullOrWhiteSpace(agentId) ||
        string.Equals(agentId.Trim(), "RiskAnalysisAgent", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(agentId.Trim(), AgentProviderKinds.FlowosRisk, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeModel(string providerName, string? model)
    {
        if (!string.IsNullOrWhiteSpace(model))
            return model.Trim();

        return providerName.Trim().ToLowerInvariant() switch
        {
            AgentProviderKinds.Anthropic => "claude-3-5-sonnet-20241022",
            AgentProviderKinds.Google => "gemini-1.5-flash",
            _ => "gpt-4o-mini"
        };
    }

    private sealed class HostedUsageReservation : IAgentUsageReservation
    {
        private readonly IFlowOsHostedLlmRuntime _runtime;
        private readonly FlowOsHostedLlmLease _lease;
        private int _finalized;

        public HostedUsageReservation(
            IFlowOsHostedLlmRuntime runtime,
            FlowOsHostedLlmLease lease)
        {
            _runtime = runtime;
            _lease = lease;
        }

        public async Task FinalizeAsync(
            bool succeeded,
            AgentTelemetry? telemetry,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _finalized, 1) != 0)
                return;

            await _runtime.FinalizeAsync(
                _lease,
                succeeded,
                Math.Max(0, telemetry?.InputTokens ?? 0),
                Math.Max(0, telemetry?.OutputTokens ?? 0),
                cancellationToken);
        }
    }

    private sealed class UsageFinalizingWorkflowAgent : IWorkflowAgent
    {
        private readonly IWorkflowAgent _inner;
        private readonly IAgentUsageReservation _reservation;

        public UsageFinalizingWorkflowAgent(
            IWorkflowAgent inner,
            IAgentUsageReservation reservation)
        {
            _inner = inner;
            _reservation = reservation;
        }

        public Task<AgentResult> ExecuteAsync(AgentContext context) =>
            ExecuteAsync(context, CancellationToken.None);

        public async Task<AgentResult> ExecuteAsync(
            AgentContext context,
            CancellationToken cancellationToken)
        {
            AgentResult result;
            try
            {
                result = await _inner.ExecuteAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await TryFinalizeFailureAsync();
                throw;
            }
            catch
            {
                await TryFinalizeFailureAsync();
                throw;
            }

            try
            {
                await _reservation.FinalizeAsync(
                    result.Success,
                    result.Telemetry,
                    CancellationToken.None);
                return result;
            }
            catch
            {
                return AgentResult.Failure(
                    AgentFailureCodes.ProviderUnavailable,
                    "Hosted provider usage accounting failed.",
                    result.Telemetry);
            }
        }

        private async Task TryFinalizeFailureAsync()
        {
            try
            {
                await _reservation.FinalizeAsync(
                    false,
                    null,
                    CancellationToken.None);
            }
            catch
            {
                // Preserve the original failure/cancellation without exposing store details.
            }
        }
    }
}
