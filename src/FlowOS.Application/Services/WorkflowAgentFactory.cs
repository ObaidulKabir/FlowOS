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

    public WorkflowAgentFactory(
        IPluginBindingRegistryService bindings,
        HttpMessageHandler? handler = null)
    {
        _bindings = bindings;
        _handler = handler;
    }

    public async Task<IWorkflowAgent> CreateAsync(
        DecisionPacket packet,
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        AgentProviderConfiguration? secrets = null;
        if (!string.IsNullOrWhiteSpace(packet.Provider?.Alias))
        {
            secrets = await _bindings.GetAgentSecretsAsync(packet.TenantId, packet.Provider.Alias, cancellationToken);
        }

        var providerName = packet.Provider?.ProviderName;
        if (IsFlowOsRisk(requestedAgentId, providerName))
            return new RiskAnalysisAgent();

        if (IsHostedLlm(providerName))
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

    private static bool IsFlowOsRisk(string requestedAgentId, string? providerName) =>
        string.Equals(providerName, AgentProviderKinds.FlowosRisk, StringComparison.OrdinalIgnoreCase) ||
        (string.IsNullOrWhiteSpace(providerName) &&
         requestedAgentId.Equals("RiskAnalysisAgent", StringComparison.OrdinalIgnoreCase));

    private static bool IsHostedLlm(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return false;

        var normalized = providerName.Trim().ToLowerInvariant();
        return normalized is AgentProviderKinds.OpenAi
            or AgentProviderKinds.Anthropic
            or AgentProviderKinds.AzureOpenAi
            or AgentProviderKinds.Google
            or AgentProviderKinds.Custom;
    }
}
