using FlowOS.Domain.Entities.ExternalAI;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IExternalAgentPlanStore
{
    Task<Guid> CreatePlanAsync(
        ExternalAgentPlanRecord plan,
        IReadOnlyList<ExternalAgentPlanStepRecord> steps,
        CancellationToken ct = default);

    Task<ExternalAgentPlanRecord?> GetLatestByChangeAsync(Guid changeId, CancellationToken ct = default);

    Task<ExternalAgentPlanRecord?> GetPlanAsync(Guid planId, CancellationToken ct = default);

    Task<IReadOnlyList<ExternalAgentPlanStepRecord>> GetPlanStepsAsync(Guid planId, CancellationToken ct = default);

    Task UpdateStepStatusAsync(
        Guid planId,
        long stepId,
        ExternalAgentPlanStepRecord step,
        CancellationToken ct = default);

    Task MarkStepResultAsync(
        Guid planId,
        long stepId,
        ExternalAgentPlanStepRecord step,
        CancellationToken ct = default);

    Task ResumeFromStepAsync(
        Guid planId,
        string? fromStepId,
        CancellationToken ct = default);
}
