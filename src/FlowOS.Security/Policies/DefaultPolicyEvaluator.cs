namespace FlowOS.Security.Policies;

public class DefaultPolicyEvaluator : IPolicyEvaluator
{
    public PolicyResult Evaluate(Policy policy, PolicyContext context)
    {
        if (policy.Name == "DenyAll")
        {
            return PolicyResult.Denied("DenyAll policy is active.");
        }

        // Capability Check Logic (Placeholder)
        // In a real implementation, we would check if the user's roles
        // contain the required capability for the requested action.
        // For now, we assume if the policy is not DenyAll, it is allowed.
        
        return PolicyResult.Allowed();
    }
}
