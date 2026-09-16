using FlowOS.Agents.Abstractions;

namespace FlowOS.Application.Common.Interfaces;

public interface IDecisionPacketBuilder
{
    Task<DecisionPacket?> BuildAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? objective = null,
        CancellationToken cancellationToken = default);

    Task<DecisionPacket?> PreviewAsync(
        Guid tenantId,
        Guid workflowClassId,
        string stepId,
        Guid? contextBindingId = null,
        IReadOnlyDictionary<string, object?>? canonicalSample = null,
        string? currentState = null,
        string? objective = null,
        CancellationToken cancellationToken = default);
}
