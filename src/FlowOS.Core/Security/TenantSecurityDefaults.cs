using System;
using System.Collections.Generic;

namespace FlowOS.Core.Security;

/// <summary>
/// Reserved tenant roles form the maximum permission grant for user and API-key
/// identities. API-key scopes can narrow, but never expand, these grants.
/// </summary>
public static class TenantSecurityDefaults
{
    public const string AdminRole = "Admin";
    public const string ApiKeyRole = "ApiKey";

    public static readonly IReadOnlyCollection<string> AdminCapabilities =
    [
        "workflow.start",
        "workflow.create",
        "workflow.read",
        "workflow.approve_public",
        "event.publish",
        "task.complete",
        "role.create",
        "agent.insight.publish",
        "iam.read",
        "iam.manage"
    ];

    public static readonly IReadOnlyCollection<string> ApiKeyCapabilities =
    [
        "workflow.start",
        "workflow.create",
        "workflow.read",
        "event.publish",
        "task.complete"
    ];

    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> ReservedRoles { get; } =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [AdminRole] = AdminCapabilities,
            [ApiKeyRole] = ApiKeyCapabilities
        };
}
