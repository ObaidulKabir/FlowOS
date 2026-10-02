using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Entities.ExternalAI;

public sealed class ExternalAgentPlanStepRecord
{
    public long Id { get; private set; }
    public Guid PlanId { get; private set; }
    public string StepId { get; private set; } = string.Empty;
    public int StepIndex { get; private set; }
    public string ToolName { get; private set; } = string.Empty;
    public string ToolArgsJson { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string DependsOnJson { get; private set; } = "[]";
    public ExternalAgentStepStatus Status { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public string? ResultSnapshotJson { get; private set; }
    public string? ErrorMessage { get; private set; }

    private ExternalAgentPlanStepRecord()
    {
    }

    public static ExternalAgentPlanStepRecord Create(
        long id,
        Guid planId,
        string stepId,
        int stepIndex,
        string toolName,
        string toolArgsJson,
        string? description,
        string dependsOnJson)
    {
        if (id < 0)
            throw new ArgumentOutOfRangeException(nameof(id), "Id cannot be negative.");
        if (planId == Guid.Empty)
            throw new ArgumentException("PlanId is required.", nameof(planId));

        return new ExternalAgentPlanStepRecord
        {
            Id = id,
            PlanId = planId,
            StepId = Required(stepId, 200, nameof(stepId)),
            StepIndex = stepIndex >= 0 ? stepIndex : throw new ArgumentOutOfRangeException(nameof(stepIndex)),
            ToolName = Required(toolName, 200, nameof(toolName)),
            ToolArgsJson = Required(toolArgsJson, nameof(toolArgsJson)),
            Description = TrimTo(description, 2000),
            DependsOnJson = string.IsNullOrWhiteSpace(dependsOnJson) ? "[]" : dependsOnJson,
            Status = ExternalAgentStepStatus.Pending,
            StartedAtUtc = null,
            FinishedAtUtc = null,
            ResultSnapshotJson = null,
            ErrorMessage = null
        };
    }

    public void MarkRunning(DateTime startedAtUtc)
    {
        if (Status != ExternalAgentStepStatus.Pending)
            throw new InvalidOperationException($"Cannot start step in status {Status}.");

        Status = ExternalAgentStepStatus.Running;
        StartedAtUtc = AsUtc(startedAtUtc);
    }

    public void MarkSucceeded(DateTime finishedAtUtc, string? resultSnapshotJson)
    {
        if (Status != ExternalAgentStepStatus.Running)
            throw new InvalidOperationException($"Cannot complete step in status {Status}.");

        var finished = AsUtc(finishedAtUtc);
        if (StartedAtUtc.HasValue && finished < StartedAtUtc.Value)
            throw new ArgumentOutOfRangeException(nameof(finishedAtUtc), "Finished cannot precede start.");

        Status = ExternalAgentStepStatus.Succeeded;
        FinishedAtUtc = finished;
        ResultSnapshotJson = TrimTo(resultSnapshotJson, 65535);
        ErrorMessage = null;
    }

    public void MarkFailed(DateTime finishedAtUtc, string errorMessage)
    {
        if (Status != ExternalAgentStepStatus.Running)
            throw new InvalidOperationException($"Cannot fail step in status {Status}.");
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("ErrorMessage is required.", nameof(errorMessage));

        var finished = AsUtc(finishedAtUtc);
        if (StartedAtUtc.HasValue && finished < StartedAtUtc.Value)
            throw new ArgumentOutOfRangeException(nameof(finishedAtUtc), "Finished cannot precede start.");

        Status = ExternalAgentStepStatus.Failed;
        FinishedAtUtc = finished;
        ErrorMessage = TrimTo(errorMessage, 4000);
    }

    public void MarkSkipped()
    {
        if (Status == ExternalAgentStepStatus.Running ||
            Status == ExternalAgentStepStatus.Succeeded)
            throw new InvalidOperationException($"Cannot skip step in status {Status}.");

        Status = ExternalAgentStepStatus.Skipped;
    }

    public void ResetToPending()
    {
        if (Status == ExternalAgentStepStatus.Running)
            throw new InvalidOperationException($"Cannot reset step in status {Status}.");

        Status = ExternalAgentStepStatus.Pending;
        StartedAtUtc = null;
        FinishedAtUtc = null;
        ResultSnapshotJson = null;
        ErrorMessage = null;
    }

    private static string Required(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        return trimmed;
    }

    private static string Required(string value, string parameterName)
    {
        if (value == null)
            throw new ArgumentNullException(parameterName);
        return value;
    }

    private static string? TrimTo(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
