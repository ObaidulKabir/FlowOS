using FlowOS.Agents.Abstractions;

namespace FlowOS.Application.Common.Interfaces;

public interface IAgentUsageReservation
{
    Task FinalizeAsync(
        bool succeeded,
        AgentTelemetry? telemetry,
        CancellationToken cancellationToken = default);
}

public sealed record ResolvedWorkflowAgent(
    IWorkflowAgent Agent,
    string ActorId,
    string RuntimeIdentifier,
    string? ProviderAlias,
    string? ProviderName,
    string? Model,
    IAgentUsageReservation? UsageReservation = null);

public interface IWorkflowAgentFactory
{
    async Task<ResolvedWorkflowAgent> ResolveAsync(
        DecisionPacket packet,
        string? requestedAgentId = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await CreateAsync(packet, requestedAgentId, cancellationToken);
        var providerName = packet.Provider?.ProviderName;
        var providerActorId = string.Equals(
                providerName,
                "flowos-hosted",
                StringComparison.OrdinalIgnoreCase)
            ? "flowos-hosted"
            : string.Equals(providerName, "flowos-risk", StringComparison.OrdinalIgnoreCase)
                ? "flowos-risk"
                : packet.Provider?.Alias ?? "flowos-risk";
        var requestedRiskIdentity =
            string.IsNullOrWhiteSpace(requestedAgentId) ||
            string.Equals(requestedAgentId.Trim(), "RiskAnalysisAgent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestedAgentId.Trim(), "flowos-risk", StringComparison.OrdinalIgnoreCase);
        var actorId = requestedRiskIdentity
            ? providerActorId
            : requestedAgentId!.Trim();
        return new ResolvedWorkflowAgent(
            agent,
            actorId,
            agent.GetType().Name,
            packet.Provider?.Alias,
            packet.Provider?.ProviderName,
            packet.Provider?.Model);
    }

    Task<IWorkflowAgent> CreateAsync(
        DecisionPacket packet,
        string? requestedAgentId,
        CancellationToken cancellationToken = default);
}
