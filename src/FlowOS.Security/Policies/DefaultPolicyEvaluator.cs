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

        // ConditionJson support: { "action": "Deny", "reason": "..." }
        if (!string.IsNullOrWhiteSpace(policy.ConditionJson) && policy.ConditionJson != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(policy.ConditionJson);
                if (doc.RootElement.TryGetProperty("action", out var actionProp))
                {
                    var action = actionProp.GetString();
                    if (string.Equals(action, "Deny", StringComparison.OrdinalIgnoreCase))
                    {
                        var reason = doc.RootElement.TryGetProperty("reason", out var reasonProp)
                            ? reasonProp.GetString()
                            : null;
                        return PolicyResult.Denied(reason ?? $"Policy '{policy.Name}' denies this action.");
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
