namespace FlowOS.Application.Common.Interfaces;

public interface ITenantBackupService
{
    Task<string> ExportJsonAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<TenantBackupRestoreResult> RestoreJsonAsync(Guid destinationTenantId, string json, CancellationToken cancellationToken = default);
}

public sealed record TenantBackupRestoreResult(
    Guid DestinationTenantId,
    Guid SourceTenantId,
    DateTime TakenAtUtc,
    string FlowOsVersion,
    string FlowOsCompatibility,
    int WorkflowClasses,
    int ContextBindings,
    int PluginBindings,
    int WorkflowInstances,
    int Events);
