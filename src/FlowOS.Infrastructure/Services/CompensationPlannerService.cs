using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Core.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

public class CompensationPlannerService : ICompensationPlannerService
{
    public CompensationPathResultDto Plan(CompensationPathRequestDto request)
    {
        var failedStepId = request.FailedStepId?.Trim() ?? string.Empty;
        var executedSteps = request.ExecutedStepIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // LIFO compensation ordering: reverse execution order.
        executedSteps.Reverse();

        var plans = new List<CompensationStepPlanDto>();
        var blocked = new List<string>();

        foreach (var stepId in executedSteps)
        {
            request.OnFailureActionsByStep.TryGetValue(stepId, out var actions);
            var resolvedActions = actions?.Where(a => !string.IsNullOrWhiteSpace(a.ActionType)).ToList()
                ?? new List<CompensationActionDto>();

            if (resolvedActions.Count == 0)
            {
                blocked.Add(stepId);
                continue;
            }

            plans.Add(new CompensationStepPlanDto(stepId, resolvedActions.Count, resolvedActions));
        }

        return new CompensationPathResultDto(
            FailedStepId: failedStepId,
            IsFullyCompensable: blocked.Count == 0,
            OrderedCompensations: plans,
            BlockedSteps: blocked);
    }
}
