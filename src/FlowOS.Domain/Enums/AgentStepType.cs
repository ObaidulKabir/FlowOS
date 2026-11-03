using System;

namespace FlowOS.Domain.Enums;

/// <summary>
/// Node kind for a step in an <c>AgentWorkflowBlueprint</c>.
///
/// ADR-006: mirrors <c>StepBlueprint.StepType</c> so the declared graph shape is an
/// explicit field rather than something a reader has to infer from which routing
/// collections happen to be populated. The dashboard graph visualiser and the
/// Phase 4B dispatcher both switch on this value.
///
/// Omitted in stored JSON means <see cref="Agent"/> — the common case.
/// </summary>
public static class AgentStepType
{
    /// <summary>A unit of work carried out by one AgentTeam. The default.</summary>
    public const string Agent = "Agent";

    /// <summary>Splits the plan into concurrent paths listed in <c>Branches</c>.</summary>
    public const string Fork = "Fork";

    /// <summary>Converges the concurrent paths listed in <c>InboundSteps</c>.</summary>
    public const string Join = "Join";

    /// <summary>Terminal node. Declares no outbound routing.</summary>
    public const string End = "End";

    /// <summary>
    /// ADR-007: a decision a person makes. This is what a <c>Consequential</c> step hands
    /// off to — without it, declaring that an agent must not commit a step left the plan
    /// with no way to name who commits it instead.
    /// </summary>
    public const string HumanTask = "HumanTask";

    public static bool IsKnown(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, Agent, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Fork, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Join, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, End, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, HumanTask, StringComparison.OrdinalIgnoreCase);

    public static bool IsHumanTask(string? value) =>
        string.Equals(value, HumanTask, StringComparison.OrdinalIgnoreCase);

    public static bool IsFork(string? value) =>
        string.Equals(value, Fork, StringComparison.OrdinalIgnoreCase);

    public static bool IsJoin(string? value) =>
        string.Equals(value, Join, StringComparison.OrdinalIgnoreCase);

    public static bool IsEnd(string? value) =>
        string.Equals(value, End, StringComparison.OrdinalIgnoreCase);

    /// <summary>Blank or missing normalises to <see cref="Agent"/>.</summary>
    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Agent : value.Trim();
}
