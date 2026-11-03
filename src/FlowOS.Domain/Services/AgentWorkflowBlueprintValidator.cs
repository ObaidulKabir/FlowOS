using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Validation;

namespace FlowOS.Domain.Services;

/// <summary>
/// Validates an <see cref="AgentWorkflowBlueprint"/> before it is stored or published.
///
/// ADR-006: mirrors <see cref="WorkflowClassValidator"/> for the agentic blueprint.
/// A published AgentWorkflowClass is immutable and dispatchable, so a blueprint with a
/// dangling <c>StartStepId</c>, an unknown routing target or an inconsistent fork/join
/// would become permanently broken. Every check here is structural — it reads only the
/// declared plan and never runtime state.
///
/// Error codes are prefixed <c>AWF-</c> so they never collide with the
/// <c>WF-</c>/<c>CON-</c> codes emitted for human workflow classes.
/// </summary>
public interface IAgentWorkflowBlueprintValidator
{
    ValidationResult Validate(AgentWorkflowBlueprint? blueprint);
}

/// <inheritdoc cref="IAgentWorkflowBlueprintValidator"/>
public sealed class AgentWorkflowBlueprintValidator : IAgentWorkflowBlueprintValidator
{
    /// <summary>Sentinel routing target meaning "the plan ends here".</summary>
    public const string TerminalTarget = "END";

    private const string JoinWaitAll = "WaitAll";
    private const string JoinWaitAny = "WaitAny";
    private const string RollbackAbandon = "Abandon";
    private const string RollbackCompensate = "Compensate";

    public ValidationResult Validate(AgentWorkflowBlueprint? blueprint)
    {
        var result = new ValidationResult();

        if (blueprint is null)
        {
            result.AddError("AWF-STR-000", "Structural", "Definition is required.", "Blueprint");
            return result;
        }

        var steps = blueprint.Steps ?? new List<AgentStepBlueprint>();

        ValidateStructure(blueprint, steps, result);
        ValidateCoordinationRules(blueprint.CoordinationRules, result);
        ValidateContextSchema(blueprint.ContextSchema, result);

        // Everything below needs a usable step-id set. Bail out when the plan has no
        // identifiable steps — further checks would only repeat STR-002/STR-003.
        var stepIds = steps
            .Where(s => !string.IsNullOrWhiteSpace(s.StepId))
            .Select(s => s.StepId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (stepIds.Count == 0) return result;

        ValidateEntryPoint(blueprint, stepIds, result);

        var reachable = WalkReachableSteps(blueprint, steps, stepIds);

        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.StepId)) continue;

            ValidateStepShape(step, result);
            ValidateRoutingTargets(step, stepIds, result);
            ValidateTeamBinding(step, result);
            ValidateAutonomy(step, result);
            ValidateSla(step, stepIds, result);
            ValidatePathLimits(step, stepIds, result);

            if (!reachable.Contains(step.StepId.Trim()))
            {
                result.AddError(
                    "AWF-COMP-003",
                    "Completeness",
                    $"Step '{step.StepId}' is unreachable from startStepId '{blueprint.StartStepId}'.",
                    "Steps");
            }
        }

        return result;
    }

    // ── Structure ─────────────────────────────────────────────────────────────

    private static void ValidateStructure(
        AgentWorkflowBlueprint blueprint,
        List<AgentStepBlueprint> steps,
        ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(blueprint.Objective))
        {
            result.AddError(
                "AWF-STR-001",
                "Structural",
                "Objective is required — it states what the plan achieves.",
                "Blueprint");
        }

        if (steps.Count == 0)
        {
            result.AddError("AWF-STR-002", "Structural", "A plan must declare at least one step.", "Steps");
            return;
        }

        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.StepId))
            {
                result.AddError("AWF-STR-003", "Structural", "Step id cannot be empty.", "Steps");
            }

            if (!AgentStepType.IsKnown(step.StepType))
            {
                result.AddError(
                    "AWF-STR-005",
                    "Structural",
                    $"Step '{step.StepId}' has unknown stepType '{step.StepType}'. " +
                    $"Allowed values: {AgentStepType.Agent}, {AgentStepType.Fork}, {AgentStepType.Join}, {AgentStepType.End}.",
                    "Steps");
            }
        }

        var duplicates = steps
            .Where(s => !string.IsNullOrWhiteSpace(s.StepId))
            .GroupBy(s => s.StepId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var duplicate in duplicates)
        {
            result.AddError(
                "AWF-STR-004",
                "Structural",
                $"Step id '{duplicate}' is declared more than once. Step ids must be unique within a plan.",
                "Steps");
        }
    }

    private static void ValidateEntryPoint(
        AgentWorkflowBlueprint blueprint,
        HashSet<string> stepIds,
        ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(blueprint.StartStepId))
        {
            result.AddError(
                "AWF-COMP-001",
                "Completeness",
                "startStepId is required — the engine needs an entry point.",
                "Blueprint");
            return;
        }

        if (!stepIds.Contains(blueprint.StartStepId.Trim()))
        {
            result.AddError(
                "AWF-COMP-002",
                "Completeness",
                $"startStepId '{blueprint.StartStepId}' does not match any declared step.",
                "Blueprint");
        }
    }

    // ── Reachability ──────────────────────────────────────────────────────────

    /// <summary>
    /// Breadth-first walk from <c>StartStepId</c> over every edge the engine can take:
    /// <c>NextSteps</c>, fork <c>Branches</c>, SLA escalation and path-limit overflow.
    /// Escalation and overflow targets count as real edges — a step reachable only when
    /// an SLA breaches is legitimately reachable, and treating it otherwise would report
    /// a false orphan.
    /// </summary>
    private static HashSet<string> WalkReachableSteps(
        AgentWorkflowBlueprint blueprint,
        List<AgentStepBlueprint> steps,
        HashSet<string> stepIds)
    {
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(blueprint.StartStepId) ||
            !stepIds.Contains(blueprint.StartStepId.Trim()))
        {
            return reachable;
        }

        // Group before ToDictionary: a plan with duplicate step ids is reported by
        // AWF-STR-004, but it must not throw here on the way to that report.
        var byId = steps
            .Where(s => !string.IsNullOrWhiteSpace(s.StepId))
            .GroupBy(s => s.StepId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var start = blueprint.StartStepId.Trim();
        var queue = new Queue<string>();
        queue.Enqueue(start);
        reachable.Add(start);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (!byId.TryGetValue(currentId, out var step)) continue;

            foreach (var target in OutboundTargets(step))
            {
                if (string.IsNullOrWhiteSpace(target)) continue;

                var trimmed = target.Trim();
                if (string.Equals(trimmed, TerminalTarget, StringComparison.OrdinalIgnoreCase)) continue;
                if (!stepIds.Contains(trimmed)) continue;   // reported by AWF-CON-*
                if (!reachable.Add(trimmed)) continue;

                queue.Enqueue(trimmed);
            }
        }

        return reachable;
    }

    private static IEnumerable<string> OutboundTargets(AgentStepBlueprint step)
    {
        if (step.NextSteps is not null)
        {
            foreach (var target in step.NextSteps.Values) yield return target;
        }

        if (step.Branches is not null)
        {
            foreach (var target in step.Branches) yield return target;
        }

        if (!string.IsNullOrWhiteSpace(step.Sla?.EscalationStepId))
        {
            yield return step.Sla!.EscalationStepId!;
        }

        if (step.PathLimits is not null)
        {
            foreach (var limit in step.PathLimits.Values)
            {
                if (!string.IsNullOrWhiteSpace(limit?.OnExceeded)) yield return limit!.OnExceeded!;
            }
        }
    }

    // ── Per-step shape ────────────────────────────────────────────────────────

    private static void ValidateStepShape(AgentStepBlueprint step, ValidationResult result)
    {
        var stepType = AgentStepType.Normalize(step.StepType);
        var branches = Distinct(step.Branches);
        var inbound = Distinct(step.InboundSteps);
        var hasExit = step.NextSteps is { Count: > 0 };

        var isFork = AgentStepType.IsFork(stepType);
        var isJoin = AgentStepType.IsJoin(stepType);
        var isEnd = AgentStepType.IsEnd(stepType);

        // A node cannot both split and converge: the engine would have no defined order
        // for the two, and the graph shape would be ambiguous to render.
        if (isFork && inbound.Count > 0)
        {
            result.AddError(
                "AWF-STEP-001",
                "StepShape",
                $"Step '{step.StepId}' is a Fork but also declares inboundSteps. " +
                "Split and converge must be separate steps.",
                "Steps");
        }

        if (!isFork && branches.Count > 0)
        {
            result.AddError(
                "AWF-STEP-002",
                "StepShape",
                $"Step '{step.StepId}' declares branches but its stepType is '{stepType}'. " +
                "branches is only meaningful on a Fork — set stepType to 'Fork'.",
                "Steps");
        }

        if (!isJoin && inbound.Count > 0)
        {
            result.AddError(
                "AWF-STEP-003",
                "StepShape",
                $"Step '{step.StepId}' declares inboundSteps but its stepType is '{stepType}'. " +
                "inboundSteps is only meaningful on a Join — set stepType to 'Join'.",
                "Steps");
        }

        if (isFork && branches.Count < 2)
        {
            result.AddError(
                "AWF-FORK-001",
                "StepShape",
                $"Fork step '{step.StepId}' must declare at least 2 distinct branches; it declares {branches.Count}.",
                "Steps");
        }

        if (isJoin)
        {
            if (inbound.Count < 2)
            {
                result.AddError(
                    "AWF-JOIN-001",
                    "StepShape",
                    $"Join step '{step.StepId}' must declare at least 2 distinct inboundSteps; it declares {inbound.Count}.",
                    "Steps");
            }

            if (!string.Equals(step.JoinPolicy, JoinWaitAll, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(step.JoinPolicy, JoinWaitAny, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    "AWF-JOIN-002",
                    "StepShape",
                    $"Join step '{step.StepId}' has unknown joinPolicy '{step.JoinPolicy}'. " +
                    $"Allowed values: {JoinWaitAll}, {JoinWaitAny}.",
                    "Steps");
            }
        }

        if (isEnd)
        {
            if (hasExit)
            {
                result.AddError(
                    "AWF-END-001",
                    "StepShape",
                    $"End step '{step.StepId}' must not declare nextSteps.",
                    "Steps");
            }

            return;
        }

        // Every non-terminal step needs a way out. A Fork routes through branches;
        // everything else routes through nextSteps.
        if (isFork)
        {
            if (branches.Count == 0)
            {
                result.AddError(
                    "AWF-COMP-004",
                    "Completeness",
                    $"Fork step '{step.StepId}' has no exit path — declare branches.",
                    "Steps");
            }

            return;
        }

        if (!hasExit)
        {
            result.AddError(
                "AWF-COMP-004",
                "Completeness",
                $"Step '{step.StepId}' has no exit path — declare nextSteps, or set stepType to 'End'.",
                "Steps");
        }
    }

    // ── Routing targets ──────────────────────────────────────────────────────

    private static void ValidateRoutingTargets(
        AgentStepBlueprint step,
        HashSet<string> stepIds,
        ValidationResult result)
    {
        if (step.NextSteps is not null)
        {
            foreach (var pair in step.NextSteps)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    result.AddError(
                        "AWF-CON-007",
                        "Consistency",
                        $"Step '{step.StepId}' declares a nextSteps entry with an empty event key.",
                        "Steps");
                }

                if (!IsKnownTarget(pair.Value, stepIds))
                {
                    result.AddError(
                        "AWF-CON-001",
                        "Consistency",
                        $"Step '{step.StepId}' routes event '{pair.Key}' to unknown step '{pair.Value}'.",
                        "Steps");
                }
            }
        }

        if (step.Branches is not null)
        {
            foreach (var target in step.Branches)
            {
                if (!IsKnownTarget(target, stepIds))
                {
                    result.AddError(
                        "AWF-CON-002",
                        "Consistency",
                        $"Step '{step.StepId}' declares unknown branch target '{target}'.",
                        "Steps");
                }
            }
        }

        if (step.InboundSteps is not null)
        {
            foreach (var inbound in step.InboundSteps)
            {
                // An inbound is always a real step: END cannot flow into anything.
                if (string.IsNullOrWhiteSpace(inbound) || !stepIds.Contains(inbound.Trim()))
                {
                    result.AddError(
                        "AWF-CON-003",
                        "Consistency",
                        $"Step '{step.StepId}' declares unknown inboundStep '{inbound}'.",
                        "Steps");
                }
            }
        }
    }

    // ── Team binding ──────────────────────────────────────────────────────────

    private static void ValidateTeamBinding(AgentStepBlueprint step, ValidationResult result)
    {
        // Fork, Join and End are engine-side control nodes; no team carries them out.
        var stepType = AgentStepType.Normalize(step.StepType);
        if (!string.Equals(stepType, AgentStepType.Agent, StringComparison.OrdinalIgnoreCase)) return;

        var hasStaticTeam = step.AgentTeamId.HasValue && step.AgentTeamId.Value != Guid.Empty;
        var hasRole = !string.IsNullOrWhiteSpace(step.TeamRole);

        if (!hasStaticTeam && !hasRole)
        {
            result.AddError(
                "AWF-TEAM-001",
                "TeamBinding",
                $"Step '{step.StepId}' binds no team — set agentTeamId for a fixed team, " +
                "or teamRole for the orchestrator to resolve at dispatch.",
                "Steps");
        }
    }

    // ── Autonomy ──────────────────────────────────────────────────────────────

    private static void ValidateAutonomy(AgentStepBlueprint step, ValidationResult result)
    {
        if (step.AutoCommit is null) return;

        // CR-11, mirrored for agent plans: a consequential step is never auto-committed,
        // whatever the confidence says. Declaring both is an authoring mistake.
        if (step.Consequential)
        {
            result.AddError(
                "AWF-AGENT-001",
                "Autonomy",
                $"Step '{step.StepId}' is consequential but configures autoCommit. " +
                "An agent may propose on a consequential step, but a person must decide. " +
                "Remove autoCommit or clear consequential.",
                "Steps");
        }

        if (step.AutoCommit.MinConfidence is <= 0 or > 1)
        {
            result.AddError(
                "AWF-AGENT-002",
                "Autonomy",
                $"Step '{step.StepId}' autoCommit.minConfidence is " +
                $"{step.AutoCommit.MinConfidence.ToString(CultureInfo.InvariantCulture)}; " +
                "use a value greater than 0 and at most 1.",
                "Steps");
        }

        var keySet = (step.NextSteps?.Keys ?? Enumerable.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var allowed in step.AutoCommit.AllowedEvents ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(allowed)) continue;

            if (!keySet.Contains(allowed.Trim()))
            {
                result.AddError(
                    "AWF-AGENT-003",
                    "Autonomy",
                    $"Step '{step.StepId}' autoCommit.allowedEvents includes '{allowed}', which is not a nextSteps key.",
                    "Steps");
            }

            if (!string.IsNullOrWhiteSpace(step.Sla?.TimeoutEvent) &&
                string.Equals(allowed.Trim(), step.Sla!.TimeoutEvent.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    "AWF-AGENT-004",
                    "Autonomy",
                    $"Step '{step.StepId}' cannot auto-commit its SLA timeout event '{allowed}'. " +
                    "A breach stays SLA-owned.",
                    "Steps");
            }
        }
    }

    // ── SLA ───────────────────────────────────────────────────────────────────

    private static void ValidateSla(
        AgentStepBlueprint step,
        HashSet<string> stepIds,
        ValidationResult result)
    {
        if (step.Sla is null) return;

        if (string.IsNullOrWhiteSpace(step.Sla.Duration))
        {
            result.AddError(
                "AWF-SLA-001",
                "Sla",
                $"Step '{step.StepId}' declares an SLA without a duration.",
                "Steps");
        }
        else if (!IsParsableDuration(step.Sla.Duration))
        {
            result.AddError(
                "AWF-SLA-002",
                "Sla",
                $"Step '{step.StepId}' SLA duration '{step.Sla.Duration}' is not a recognised duration. " +
                "Use an ISO-8601 duration such as 'PT4H', or shorthand such as '30m', '4h', '2d'.",
                "Steps");
        }

        if (string.IsNullOrWhiteSpace(step.Sla.TimeoutEvent))
        {
            result.AddError(
                "AWF-SLA-003",
                "Sla",
                $"Step '{step.StepId}' declares an SLA without a timeoutEvent — " +
                "the engine would have no event to publish on breach.",
                "Steps");
        }

        if (!string.IsNullOrWhiteSpace(step.Sla.EscalationStepId) &&
            !IsKnownTarget(step.Sla.EscalationStepId, stepIds))
        {
            result.AddError(
                "AWF-CON-004",
                "Consistency",
                $"Step '{step.StepId}' SLA escalates to unknown step '{step.Sla.EscalationStepId}'.",
                "Steps");
        }
    }

    // ── Path limits ───────────────────────────────────────────────────────────

    private static void ValidatePathLimits(
        AgentStepBlueprint step,
        HashSet<string> stepIds,
        ValidationResult result)
    {
        if (step.PathLimits is null || step.PathLimits.Count == 0) return;

        var knownKeys = (step.NextSteps?.Keys ?? Enumerable.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in step.PathLimits)
        {
            if (!knownKeys.Contains(pair.Key))
            {
                result.AddError(
                    "AWF-CON-005",
                    "Consistency",
                    $"Step '{step.StepId}' pathLimits key '{pair.Key}' does not match a nextSteps event.",
                    "Steps");
                continue;
            }

            var limit = pair.Value;
            if (limit is null) continue;

            if (limit.MaxTravels < 1)
            {
                result.AddError(
                    "AWF-LOOP-001",
                    "PathTravel",
                    $"Step '{step.StepId}' pathLimits['{pair.Key}'].maxTravels is {limit.MaxTravels}; it must be at least 1.",
                    "Steps");
            }

            if (!string.IsNullOrWhiteSpace(limit.OnExceeded) &&
                !IsKnownTarget(limit.OnExceeded, stepIds))
            {
                result.AddError(
                    "AWF-CON-006",
                    "Consistency",
                    $"Step '{step.StepId}' pathLimits['{pair.Key}'].onExceeded '{limit.OnExceeded}' is not a known step.",
                    "Steps");
            }
        }
    }

    // ── Coordination rules ───────────────────────────────────────────────────

    private static void ValidateCoordinationRules(
        AgentCoordinationRulesBlueprint? rules,
        ValidationResult result)
    {
        if (rules is null) return;

        if (rules.MaxGlobalRetries < 0)
        {
            result.AddError(
                "AWF-RULE-001",
                "CoordinationRules",
                $"coordinationRules.maxGlobalRetries is {rules.MaxGlobalRetries}; it cannot be negative.",
                "CoordinationRules");
        }

        if (!string.Equals(rules.RollbackStrategy, RollbackAbandon, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rules.RollbackStrategy, RollbackCompensate, StringComparison.OrdinalIgnoreCase))
        {
            result.AddError(
                "AWF-RULE-002",
                "CoordinationRules",
                $"coordinationRules.rollbackStrategy '{rules.RollbackStrategy}' is unknown. " +
                $"Allowed values: {RollbackAbandon}, {RollbackCompensate}.",
                "CoordinationRules");
        }

        if (rules.MaxParallelSteps < 0)
        {
            result.AddError(
                "AWF-RULE-003",
                "CoordinationRules",
                $"coordinationRules.maxParallelSteps is {rules.MaxParallelSteps}; use 0 for unlimited.",
                "CoordinationRules");
        }

        if (!string.IsNullOrWhiteSpace(rules.GlobalSla) && !IsParsableDuration(rules.GlobalSla))
        {
            result.AddError(
                "AWF-RULE-004",
                "CoordinationRules",
                $"coordinationRules.globalSla '{rules.GlobalSla}' is not a recognised duration. " +
                "Use an ISO-8601 duration such as 'PT8H'.",
                "CoordinationRules");
        }
    }

    private static void ValidateContextSchema(string? contextSchema, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(contextSchema)) return;

        try
        {
            using var parsed = JsonDocument.Parse(contextSchema);
        }
        catch (JsonException)
        {
            result.AddError(
                "AWF-SCHEMA-001",
                "ContextSchema",
                "contextSchema is not valid JSON.",
                "Blueprint");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Accepts the duration shapes the existing workflow engine already parses
    /// (<c>WorkflowCommandHandlers.ParseDuration</c>): an ISO-8601 duration, the
    /// shorthand forms <c>90s</c> / <c>30m</c> / <c>4h</c> / <c>2d</c>, a bare number of
    /// seconds, and <c>hh:mm:ss</c>. Validating more strictly than the engine parses
    /// would reject blueprints that run correctly.
    /// </summary>
    private static bool IsParsableDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();
        if (text.StartsWith('-') || text.StartsWith('+')) text = text[1..].Trim();
        if (text.Length == 0) return false;

        var suffix = text[^1];
        if (suffix is 's' or 'S' or 'm' or 'M' or 'h' or 'H' or 'd' or 'D')
        {
            var head = text[..^1];
            if (double.TryParse(head, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return true;
        if (text.Contains(':') && TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out _)) return true;

        try
        {
            System.Xml.XmlConvert.ToTimeSpan(text);
            return true;
        }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static bool IsKnownTarget(string? target, HashSet<string> stepIds) =>
        !string.IsNullOrWhiteSpace(target) &&
        (string.Equals(target.Trim(), TerminalTarget, StringComparison.OrdinalIgnoreCase) ||
         stepIds.Contains(target.Trim()));

    private static List<string> Distinct(List<string>? values) =>
        (values ?? new List<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
