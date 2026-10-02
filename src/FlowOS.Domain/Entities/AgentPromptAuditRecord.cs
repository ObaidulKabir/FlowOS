namespace FlowOS.Domain.Entities;

public sealed class AgentPromptAuditRecord
{
    public Guid AuditId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? WorkflowInstanceId { get; private set; }
    public string? StepId { get; private set; }
    public string? ProviderAlias { get; private set; }
    public string ProviderName { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }
    public string? SystemPrompt { get; private set; }
    public string? UserPrompt { get; private set; }
    public string RawRequestPayload { get; private set; } = string.Empty;
    public string? RawResponsePayload { get; private set; }
    public string? FailureCode { get; private set; }
    public int? HttpStatusCode { get; private set; }
    public int Iteration { get; private set; }
    public long? InputTokens { get; private set; }
    public long? OutputTokens { get; private set; }
    public long DurationMs { get; private set; }
    public Guid? ExecutionId { get; private set; }
    public Guid? CorrelationId { get; private set; }

    private AgentPromptAuditRecord()
    {
    }

    public AgentPromptAuditRecord(
        Guid auditId,
        Guid tenantId,
        string providerName,
        string model,
        DateTime recordedAtUtc,
        string rawRequestPayload,
        long durationMs,
        int iteration = 0,
        Guid? workflowInstanceId = null,
        string? stepId = null,
        string? providerAlias = null,
        string? systemPrompt = null,
        string? userPrompt = null,
        string? rawResponsePayload = null,
        string? failureCode = null,
        int? httpStatusCode = null,
        long? inputTokens = null,
        long? outputTokens = null,
        Guid? executionId = null,
        Guid? correlationId = null)
    {
        if (auditId == Guid.Empty)
            throw new ArgumentException("AuditId is required.", nameof(auditId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        AuditId = auditId;
        TenantId = tenantId;
        WorkflowInstanceId = workflowInstanceId;
        StepId = Optional(stepId, 200);
        ProviderAlias = Optional(providerAlias, 200);
        ProviderName = Required(providerName, 200, nameof(providerName));
        Model = Required(model, 200, nameof(model));
        RecordedAtUtc = AsUtc(recordedAtUtc);
        SystemPrompt = systemPrompt;
        UserPrompt = userPrompt;
        RawRequestPayload = Required(rawRequestPayload, nameof(rawRequestPayload));
        RawResponsePayload = rawResponsePayload;
        FailureCode = Optional(failureCode, 100);
        HttpStatusCode = httpStatusCode;
        Iteration = iteration;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        DurationMs = durationMs;
        ExecutionId = executionId;
        CorrelationId = correlationId;
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

    private static string? Optional(string? value, int maxLength)
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
