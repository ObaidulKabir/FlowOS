using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Domain.Blueprints;

namespace FlowOS.Domain.Services;

/// <summary>
/// Repeatable workflow paths (retry-password, resubmit, pin re-entry) must be counted.
/// Acyclic edges are unlimited. Cyclic edges default to <see cref="DefaultMaxTravels"/> unless
/// the step declares <c>pathLimits</c>.
/// </summary>
public static class PathTravelRules
{
    public const int DefaultMaxTravels = 5;
    public const int AbsoluteMaxTravels = 100;

    public readonly record struct PathEdge(string FromStepId, string EventKey, string ToStepId);

    public static string EdgeKey(string fromStepId, string eventKey, string toStepId)
        => $"{fromStepId}|{eventKey}|{toStepId}";

    public static IReadOnlyList<PathEdge> CollectEdges(IEnumerable<StepBlueprint> steps)
    {
        var edges = new List<PathEdge>();
        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.StepId))
                continue;

            if (step.NextSteps != null)
            {
                foreach (var pair in step.NextSteps)
                {
                    if (string.IsNullOrWhiteSpace(pair.Value))
                        continue;
                    edges.Add(new PathEdge(step.StepId, pair.Key, pair.Value));
                }
            }

            if (string.Equals(step.StepType, "Decision", StringComparison.OrdinalIgnoreCase) && step.Conditions != null)
            {
                foreach (var pair in step.Conditions)
                {
                    if (string.IsNullOrWhiteSpace(pair.Value))
                        continue;
                    edges.Add(new PathEdge(step.StepId, pair.Key, pair.Value));
                }
            }
        }

        return edges;
    }

    public static IReadOnlyList<PathEdge> CollectEdges(
        IEnumerable<(string StepId, string StepType, IDictionary<string, string>? NextSteps, IDictionary<string, string>? Conditions)> steps)
    {
        var edges = new List<PathEdge>();
        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.StepId))
                continue;

            if (step.NextSteps != null)
            {
                foreach (var pair in step.NextSteps)
                {
                    if (string.IsNullOrWhiteSpace(pair.Value))
                        continue;
                    edges.Add(new PathEdge(step.StepId, pair.Key, pair.Value));
                }
            }

            if (string.Equals(step.StepType, "Decision", StringComparison.OrdinalIgnoreCase) && step.Conditions != null)
            {
                foreach (var pair in step.Conditions)
                {
                    if (string.IsNullOrWhiteSpace(pair.Value))
                        continue;
                    edges.Add(new PathEdge(step.StepId, pair.Key, pair.Value));
                }
            }
        }

        return edges;
    }

    public static HashSet<string> FindCyclicEdgeKeys(IReadOnlyList<PathEdge> edges)
    {
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (string.Equals(edge.ToStepId, "END", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!adj.TryGetValue(edge.FromStepId, out var nexts))
            {
                nexts = new HashSet<string>(StringComparer.Ordinal);
                adj[edge.FromStepId] = nexts;
            }

            nexts.Add(edge.ToStepId);
        }

        var cyclic = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (string.Equals(edge.ToStepId, "END", StringComparison.OrdinalIgnoreCase))
                continue;

            if (CanReach(adj, edge.ToStepId, edge.FromStepId))
                cyclic.Add(EdgeKey(edge.FromStepId, edge.EventKey, edge.ToStepId));
        }

        return cyclic;
    }

    public static int ResolveMaxTravels(int? declared, bool isCyclic)
    {
        if (declared.HasValue)
            return Math.Clamp(declared.Value, 1, AbsoluteMaxTravels);

        return isCyclic ? DefaultMaxTravels : int.MaxValue;
    }

    public static bool TryGetDeclaredLimit(
        IReadOnlyDictionary<string, PathTravelLimitBlueprint>? pathLimits,
        string eventKey,
        out PathTravelLimitBlueprint? limit)
    {
        limit = null;
        if (pathLimits == null || pathLimits.Count == 0)
            return false;

        foreach (var pair in pathLimits)
        {
            if (string.Equals(pair.Key, eventKey, StringComparison.OrdinalIgnoreCase))
            {
                limit = pair.Value;
                return true;
            }
        }

        return false;
    }

    private static bool CanReach(
        IReadOnlyDictionary<string, HashSet<string>> adj,
        string from,
        string to)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
            return true;

        var seen = new HashSet<string>(StringComparer.Ordinal) { from };
        var queue = new Queue<string>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adj.TryGetValue(current, out var nexts))
                continue;

            foreach (var next in nexts)
            {
                if (!seen.Add(next))
                    continue;
                if (string.Equals(next, to, StringComparison.Ordinal))
                    return true;
                queue.Enqueue(next);
            }
        }

        return false;
    }
}
