using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Core.Common.Interfaces;

public sealed record TenantApplicationDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int ActiveKeyCount,
    int WorkflowCount = 0);

public static class TenantApplicationErrorCodes
{
    public const string Invalid = "APP-ARG-001";
    public const string NotFound = "APP-NOTFOUND-001";
    public const string NameTaken = "APP-NAME-001";
    public const string InUse = "APP-INUSE-001";
}

public sealed class TenantApplicationException : Exception
{
    public TenantApplicationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// Applications group a tenant's FlowOS API keys (and, in later changes, its workflows and agents).
/// Every method is tenant-scoped. Failures throw <see cref="TenantApplicationException"/> with a code
/// from <see cref="TenantApplicationErrorCodes"/>.
/// </summary>
public interface ITenantApplicationService
{
    Task<IReadOnlyList<TenantApplicationDto>> ListAsync(Guid tenantId, CancellationToken ct = default);

    Task<TenantApplicationDto?> GetAsync(Guid tenantId, Guid applicationId, CancellationToken ct = default);

    /// <summary>Names are unique per tenant, compared case-insensitively.</summary>
    Task<TenantApplicationDto> CreateAsync(Guid tenantId, string name, string? description, CancellationToken ct = default);

    /// <summary>Renaming also relabels the ApplicationName shown on the application's keys.</summary>
    Task<TenantApplicationDto> UpdateAsync(Guid tenantId, Guid applicationId, string name, string? description, CancellationToken ct = default);

    /// <summary>
    /// Refused with APP-INUSE-001 while the application has an active key, any workflow, or any prompt or
    /// agent profile binding. Revoked or expired keys are unlinked first, so they never block deletion.
    /// </summary>
    Task DeleteAsync(Guid tenantId, Guid applicationId, CancellationToken ct = default);

    /// <summary>
    /// Moves a workflow to an application (or unassigns it with null). Every version of that workflow -
    /// the tenant's workflow classes with the same name - moves together. Returns the versions moved.
    /// APP-NOTFOUND-001 when the application or the tenant's workflow class does not exist.
    /// </summary>
    Task<int> AssignWorkflowClassAsync(Guid tenantId, Guid workflowClassId, Guid? applicationId, CancellationToken ct = default);
}
