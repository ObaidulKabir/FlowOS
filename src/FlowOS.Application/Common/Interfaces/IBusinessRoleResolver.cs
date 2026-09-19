using System.Collections.Generic;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Common.Interfaces;

/// <summary>
/// Resolves which of a workflow's declared business-context roles a caller currently holds for one
/// specific running instance. This is deliberately separate from FlowOS's own tenant/IAM role system
/// (ICurrentUser.Roles / Role / TenantUserRole): business roles belong to the application the
/// workflow was designed to model, and membership in them is computed fresh, per instance, from
/// that instance's own state and business payload — never from a standing FlowOS security grant.
/// </summary>
public interface IBusinessRoleResolver
{
    /// <summary>
    /// Returns the business-role names (from <see cref="WorkflowDefinition.BusinessRoles"/>) that
    /// <paramref name="callerRef"/> currently holds for <paramref name="instance"/>, evaluated fresh
    /// against the instance's own <see cref="WorkflowInstance.RoleAssignments"/> and/or
    /// <paramref name="businessPayload"/> depending on each role's ResolutionType.
    /// </summary>
    IReadOnlyList<string> ResolveCallerRoles(
        WorkflowDefinition definition,
        WorkflowInstance instance,
        Dictionary<string, object>? businessPayload,
        string? callerRef);
}
