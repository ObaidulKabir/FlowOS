using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowOS.Core.Security;

/// <summary>
/// Canonical translation between API-key scopes (colon notation) and runtime
/// capabilities (dot notation). Scopes are an outer permission boundary; they
/// never grant a capability that the credential's tenant role does not hold.
/// </summary>
public static class ApiKeyScopeCatalog
{
    public const string FullAccess = "*";
    public const string AdminWildcard = "admin:*";

    private static readonly IReadOnlyDictionary<string, string[]> CapabilityMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["workflow:start"] = new[] { "workflow.start" },
            ["workflow:read"] = new[] { "workflow.read" },
            ["event:publish"] = new[] { "event.publish", "event.publish.*" },
            ["task:complete"] = new[] { "task.complete" },
            ["governance:manage"] = new[] { "workflow.create", "workflow.approve_public" },
            ["iam:read"] = new[] { "iam.read" },
            ["iam:manage"] = new[] { "iam.read", "iam.manage" }
        };

    public static IReadOnlyCollection<string> KnownScopes => CapabilityMap.Keys.ToArray();

    public static bool HasFullAccess(IEnumerable<string>? scopes)
        => NormalizeScopes(scopes).Any(scope =>
            string.Equals(scope, FullAccess, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scope, AdminWildcard, StringComparison.OrdinalIgnoreCase));

    public static HashSet<string> MapToCapabilities(IEnumerable<string>? scopes)
    {
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in NormalizeScopes(scopes))
        {
            if (CapabilityMap.TryGetValue(scope, out var mapped))
            {
                foreach (var capability in mapped)
                    capabilities.Add(capability);
                continue;
            }

            if (scope.EndsWith(":*", StringComparison.Ordinal))
            {
                capabilities.Add(scope[..^2] + ".*");
                continue;
            }

            // Backward compatibility for keys that stored dot-notation values.
            if (scope.Contains('.'))
                capabilities.Add(scope);
        }

        return capabilities;
    }

    public static bool AllowsCapability(IEnumerable<string>? scopes, string capability)
    {
        if (string.IsNullOrWhiteSpace(capability))
            return false;

        var normalizedScopes = NormalizeScopes(scopes);
        if (HasFullAccess(normalizedScopes))
            return true;

        var required = capability.Trim();
        foreach (var allowed in MapToCapabilities(normalizedScopes))
        {
            if (string.Equals(allowed, required, StringComparison.OrdinalIgnoreCase))
                return true;

            if (allowed.EndsWith(".*", StringComparison.Ordinal) &&
                required.StartsWith(allowed[..^1], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> NormalizeScopes(IEnumerable<string>? scopes)
        => (scopes ?? Enumerable.Empty<string>())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
