using System.Collections.Generic;

namespace FlowOS.Core.Common.Interfaces;

public record CompensationActionDto(
    string StepId,
    string Hook,
    string ActionType,
    string? Target,
    string? Url,
    string? Condition,
    string? Capability = null);

public record CompensationPathRequestDto(
    string FailedStepId,
    List<string> ExecutedStepIds,
    Dictionary<string, List<CompensationActionDto>> OnFailureActionsByStep);

public record CompensationStepPlanDto(
    string StepId,
    int ActionCount,
    List<CompensationActionDto> Actions);

public record CompensationPathResultDto(
    string FailedStepId,
    bool IsFullyCompensable,
    List<CompensationStepPlanDto> OrderedCompensations,
    List<string> BlockedSteps);

public interface ICompensationPlannerService
{
    CompensationPathResultDto Plan(CompensationPathRequestDto request);
}
