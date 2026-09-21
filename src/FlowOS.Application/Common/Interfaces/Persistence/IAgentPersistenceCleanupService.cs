namespace FlowOS.Application.Common.Interfaces.Persistence;

public sealed class AgentPersistenceRetentionOptions
{
    public const string ConfigurationSection = "FlowOS:AgentPersistence:Retention";

    public int TerminalJobDays { get; set; } = 14;
    public int ExpiredLeaseGraceMinutes { get; set; } = 60;
    public int UsageDays { get; set; } = 35;
    public int ExecutionDays { get; set; } = 90;
    public int BatchSize { get; set; } = 1000;
}

public sealed record AgentPersistenceCleanupResult(
    int JobsDeleted,
    int LeasesDeleted,
    int UsageRowsDeleted,
    int ExecutionRecordsDeleted);

public interface IAgentPersistenceCleanupService
{
    Task<AgentPersistenceCleanupResult> CleanupAsync(
        CancellationToken cancellationToken = default);
}
