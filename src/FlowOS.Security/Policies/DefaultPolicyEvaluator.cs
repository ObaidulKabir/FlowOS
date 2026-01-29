namespace FlowOS.Security.Policies;

public class DefaultPolicyEvaluator : IPolicyEvaluator
{
    public PolicyResult Evaluate(Policy policy, PolicyContext context)
    {
        if (policy.Name == "DenyAll")
        {
            return PolicyResult.Denied("DenyAll policy is active.");
        }
        
        return PolicyResult.Allowed();
    }
}
