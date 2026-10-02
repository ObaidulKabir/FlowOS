using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

internal sealed class ExternalAgentPlanStore : IExternalAgentPlanStore
{
    private readonly FlowOSDbContext _db;

    public ExternalAgentPlanStore(FlowOSDbContext db) => _db = db;

    public async Task<Guid> CreatePlanAsync(
        ExternalAgentPlanRecord plan,
        IReadOnlyList<ExternalAgentPlanStepRecord> steps,
        CancellationToken ct = default)
    {
        _db.ExternalAgentPlanRecords.Add(plan);
        _db.ExternalAgentPlanStepRecords.AddRange(steps);
        await _db.SaveChangesAsync(ct);
        return plan.Id;
    }

    public Task<ExternalAgentPlanRecord?> GetLatestByChangeAsync(Guid changeId, CancellationToken ct = default)
    {
        return _db.ExternalAgentPlanRecords
            .Where(p => p.ChangeId == changeId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public Task<ExternalAgentPlanRecord?> GetPlanAsync(Guid planId, CancellationToken ct = default)
    {
        return _db.ExternalAgentPlanRecords.FindAsync([planId], ct).AsTask();
    }

    public async Task<IReadOnlyList<ExternalAgentPlanStepRecord>> GetPlanStepsAsync(
        Guid planId,
        CancellationToken ct = default)
    {
        return await _db.ExternalAgentPlanStepRecords
            .Where(s => s.PlanId == planId)
            .OrderBy(s => s.StepIndex)
            .ToListAsync(ct);
    }

    public async Task UpdateStepStatusAsync(
        Guid planId,
        long stepId,
        ExternalAgentPlanStepRecord step,
        CancellationToken ct = default)
    {
        _db.ExternalAgentPlanStepRecords.Update(step);
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkStepResultAsync(
        Guid planId,
        long stepId,
        ExternalAgentPlanStepRecord step,
        CancellationToken ct = default)
    {
        _db.ExternalAgentPlanStepRecords.Update(step);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ResumeFromStepAsync(
        Guid planId,
        string? fromStepId,
        CancellationToken ct = default)
    {
        var steps = await _db.ExternalAgentPlanStepRecords
            .Where(s => s.PlanId == planId)
            .OrderBy(s => s.StepIndex)
            .ToListAsync(ct);

        if (steps.Count == 0)
            return;

        int startIndex = 0;
        if (!string.IsNullOrWhiteSpace(fromStepId))
        {
            var startStep = steps.FirstOrDefault(s => s.StepId == fromStepId);
            if (startStep != null)
                startIndex = steps.IndexOf(startStep);
        }
        else
        {
            var firstFailed = steps.FirstOrDefault(s => s.Status == ExternalAgentStepStatus.Failed);
            if (firstFailed != null)
                startIndex = steps.IndexOf(firstFailed);
        }

        for (int i = startIndex; i < steps.Count; i++)
        {
            var s = steps[i];
            if (s.Status == ExternalAgentStepStatus.Failed ||
                s.Status == ExternalAgentStepStatus.Skipped ||
                s.Status == ExternalAgentStepStatus.Pending)
            {
                s.ResetToPending();
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
