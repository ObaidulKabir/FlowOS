using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;

namespace FlowOS.Application.Services;

public sealed class WorkflowAgentFactory : IWorkflowAgentFactory
{
    private readonly IPluginBindingRegistryService _bindings;
    private readonly HttpMessageHandler? _handler;
    private readonly IFlowOsHostedLlmRuntime? _hosted;

    public WorkflowAgentFactory(
        IPluginBindingRegistryService bindings,
        HttpMessageHandler? handler = null,
        IFlowOsHostedLlmRuntime? hosted = null)
    {
        _bindings = bindings;
        _handler = handler;
        _hosted = hosted;
    }

    public async Task<IWorkflowAgent> CreateAsync(
        DecisionPacket packet,
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        AgentProviderConfiguration? secrets = null;
        if (!string.IsNullOrWhiteSpace(packet.Provider?.Alias) &&
            !AgentProviderKinds.IsFlowOsHosted(packet.Provider.ProviderName) &&
            !AgentProviderKinds.IsFlowOsHosted(packet.Provider.Alias))
        {
            secrets = await _bindings.GetAgentSecretsAsync(packet.TenantId, packet.Provider.Alias, cancellationToken);
        }

        var providerName = packet.Provider?.ProviderName;

        if (IsExplicitFlowOsRisk(providerName))
            return new RiskAnalysisAgent();

        if (ShouldUseFlowOsHosted(providerName))
        {
            if (_hosted == null)
            {
                throw new InvalidOperationException(
                    $"{FlowOsHostedLlmCodes.Unavailable}: {FlowOsHostedLlmCodes.UnavailableMessage}");
            }

            var lease = await _hosted.TryLeaseAsync(packet.TenantId, cancellationToken);
            if (!lease.Allowed)
            {
                throw new InvalidOperationException(
                    $"{lease.Code ?? FlowOsHostedLlmCodes.Unavailable}: {lease.Message ?? FlowOsHostedLlmCodes.UnavailableMessage}");
            }

            return new TenantLlmWorkflowAgent(
                AgentProviderKinds.OpenAi,
                lease.Model,
                lease.Endpoint,
                lease.ApiKey,
                _handler);
        }

        if (AgentProviderKinds.IsByoLlm(providerName))
        {
            return new TenantLlmWorkflowAgent(
                providerName!,
                secrets?.Model ?? packet.Provider?.Model,
                secrets?.Endpoint ?? packet.Provider?.Endpoint,
                secrets?.ApiKey,
                _handler);
        }

        if (requestedAgentId.Equals("RiskAnalysisAgent", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(requestedAgentId))
        {
            return new RiskAnalysisAgent();
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
}
