using System;
using System.Linq;
using FlowOS.Domain.Services;
using FlowOS.Workflows.Domain;

namespace FlowOS.Workflows.Engine;

public sealed record PathTravelDecision(
    bool Proceed,
    string EdgeKey,
    int NextCount,
    int MaxTravels,
    string NextStepId,
    string? FailureReason);

public static class PathTravelGuard
{
    public static PathTravelDecision Evaluate(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        WorkflowStepDefinition currentStep,
        string eventKey,
        string intendedNextStepId)
    {
        var edgeKey = PathTravelRules.EdgeKey(currentStep.StepId, eventKey, intendedNextStepId);
        if (string.Equals(intendedNextStepId, "END", StringComparison.OrdinalIgnoreCase))
        {
            return new PathTravelDecision(true, edgeKey, 0, int.MaxValue, intendedNextStepId, null);
        }

        var edges = PathTravelRules.CollectEdges(
            definition.Steps.Select(step => (
                step.StepId,
                step.StepType.ToString(),
                (IDictionary<string, string>?)step.NextSteps,
                (IDictionary<string, string>?)step.Conditions)));
        var cyclic = PathTravelRules.FindCyclicEdgeKeys(edges);
        var isCyclic = cyclic.Contains(edgeKey)
            || string.Equals(currentStep.StepId, intendedNextStepId, StringComparison.Ordinal);

        int? declared = null;
        string? onExceeded = null;
        if (currentStep.PathLimits != null
            && currentStep.PathLimits.TryGetValue(eventKey, out var limit)
            && limit != null)
        {
            declared = limit.MaxTravels;
            onExceeded = limit.OnExceeded;
        }

        var maxTravels = PathTravelRules.ResolveMaxTravels(declared, isCyclic);
        if (maxTravels == int.MaxValue)
        {
            return new PathTravelDecision(true, edgeKey, instance.GetPathTravelCount(edgeKey) + 1, maxTravels, intendedNextStepId, null);
        }

        var nextCount = instance.GetPathTravelCount(edgeKey) + 1;
        if (nextCount <= maxTravels)
        {
            return new PathTravelDecision(true, edgeKey, nextCount, maxTravels, intendedNextStepId, null);
        }

        if (!string.IsNullOrWhiteSpace(onExceeded))
        {
            if (string.Equals(onExceeded, "END", StringComparison.OrdinalIgnoreCase)
                || definition.Steps.Any(step => string.Equals(step.StepId, onExceeded, StringComparison.Ordinal)))
            {
                return new PathTravelDecision(true, edgeKey, nextCount, maxTravels, onExceeded, null);
            }

            return new PathTravelDecision(
                false,
                edgeKey,
                nextCount,
                maxTravels,
                intendedNextStepId,
                $"Path travel limit exceeded for '{currentStep.StepId}' --{eventKey}--> '{intendedNextStepId}' ({nextCount}/{maxTravels}), and onExceeded step '{onExceeded}' was not found.");
        }

        return new PathTravelDecision(
            false,
            edgeKey,
            nextCount,
            maxTravels,
            intendedNextStepId,
            $"Path travel limit exceeded for '{currentStep.StepId}' --{eventKey}--> '{intendedNextStepId}' ({nextCount}/{maxTravels}). Repeatable paths such as retry-password must declare pathLimits.maxTravels and an onExceeded exit.");
    }
}
