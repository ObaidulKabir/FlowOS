namespace FlowOS.Domain.Entities.ExternalAI;

public sealed class ExternalAgentPlanRecord
{
    public Guid Id { get; private set; }
    public Guid ChangeId { get; private set; }
    public Guid TenantId { get; private set; }
    public string AgentProfileId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public string PlanStepsJson { get; private set; } = string.Empty;
    public int PlanVersion { get; private set; } = 1;

    private ExternalAgentPlanRecord()
    {
    }

    public static ExternalAgentPlanRecord Create(
        Guid id,
        Guid changeId,
        Guid tenantId,
        string agentProfileId,
        DateTime createdAtUtc,
        string planStepsJson,
        int planVersion = 1)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Id is required.", nameof(id));
        if (changeId == Guid.Empty)
            throw new ArgumentException("ChangeId is required.", nameof(changeId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        return new ExternalAgentPlanRecord
        {
            Id = id,
            ChangeId = changeId,
            TenantId = tenantId,
            AgentProfileId = Required(agentProfileId, 200, nameof(agentProfileId)),
            CreatedAtUtc = AsUtc(createdAtUtc),
            PlanStepsJson = Required(planStepsJson, nameof(planStepsJson)),
            PlanVersion = planVersion > 0 ? planVersion : 1
        };
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

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
