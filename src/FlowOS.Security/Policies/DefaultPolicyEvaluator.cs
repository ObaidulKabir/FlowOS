using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FlowOS.Security.Policies;

public class DefaultPolicyEvaluator : IPolicyEvaluator
{
    public PolicyResult Evaluate(Policy policy, PolicyContext context)
    {
        // Legacy: literal DenyAll name always denies.
        if (string.Equals(policy.Name, "DenyAll", StringComparison.Ordinal))
        {
            return PolicyResult.Denied("DenyAll policy is active.");
        }

        // ConditionJson evaluation
        if (!string.IsNullOrWhiteSpace(policy.ConditionJson) && policy.ConditionJson != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(policy.ConditionJson);
                var root = doc.RootElement;

                // 1. Explicit Action: Deny
                if (root.TryGetProperty("action", out var actionProp))
                {
                    var action = actionProp.GetString();
                    if (string.Equals(action, "Deny", StringComparison.OrdinalIgnoreCase))
                    {
                        var reason = root.TryGetProperty("reason", out var reasonProp)
                            ? reasonProp.GetString()
                            : null;
                        return PolicyResult.Denied(reason ?? $"Policy '{policy.Name}' denies this action.");
                    }
                }

                // 2. Denied Roles Check
                if (root.TryGetProperty("deniedRoles", out var deniedRolesProp) && deniedRolesProp.ValueKind == JsonValueKind.Array)
                {
                    var userRoles = context.Roles ?? new List<string>();
                    foreach (var elem in deniedRolesProp.EnumerateArray())
                    {
                        var deniedRole = elem.GetString();
                        if (!string.IsNullOrEmpty(deniedRole) && userRoles.Contains(deniedRole, StringComparer.OrdinalIgnoreCase))
                        {
                            var reason = root.TryGetProperty("reason", out var reasonProp) ? reasonProp.GetString() : null;
                            return PolicyResult.Denied(reason ?? $"Role '{deniedRole}' is explicitly denied by policy '{policy.Name}'.");
                        }
                    }
                }

                // 3. Required Roles Check
                if (root.TryGetProperty("requiredRoles", out var reqRolesProp) && reqRolesProp.ValueKind == JsonValueKind.Array)
                {
                    var userRoles = context.Roles ?? new List<string>();
                    var requiredList = reqRolesProp.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    if (requiredList.Any() && !requiredList.Any(r => userRoles.Contains(r!, StringComparer.OrdinalIgnoreCase)))
                    {
                        var reason = root.TryGetProperty("reason", out var reasonProp) ? reasonProp.GetString() : null;
                        return PolicyResult.Denied(reason ?? $"User lacks required roles for policy '{policy.Name}'. Required: [{string.Join(", ", requiredList)}]");
                    }
                }

                // 4. Allowed Days Check
                if (root.TryGetProperty("allowedDays", out var allowedDaysProp) && allowedDaysProp.ValueKind == JsonValueKind.Array)
                {
                    var currentDay = DateTime.UtcNow.DayOfWeek.ToString();
                    var allowedDays = allowedDaysProp.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    if (allowedDays.Any() && !allowedDays.Contains(currentDay, StringComparer.OrdinalIgnoreCase))
                    {
                        var reason = root.TryGetProperty("reason", out var reasonProp) ? reasonProp.GetString() : null;
                        return PolicyResult.Denied(reason ?? $"Action not allowed on {currentDay} by policy '{policy.Name}'.");
                    }
                }

                // 5. Denied Command Types
                if (root.TryGetProperty("deniedCommandTypes", out var deniedCmdsProp) && deniedCmdsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in deniedCmdsProp.EnumerateArray())
                    {
                        var deniedCmd = elem.GetString();
                        if (!string.IsNullOrEmpty(deniedCmd) && string.Equals(deniedCmd, context.CommandType, StringComparison.OrdinalIgnoreCase))
                        {
                            var reason = root.TryGetProperty("reason", out var reasonProp) ? reasonProp.GetString() : null;
                            return PolicyResult.Denied(reason ?? $"Command '{context.CommandType}' is denied by policy '{policy.Name}'.");
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed condition: fail closed for safety.
                return PolicyResult.Denied($"Policy '{policy.Name}' has invalid ConditionJson.");
            }
        }

        return PolicyResult.Allowed();
    }
}
