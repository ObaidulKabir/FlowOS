namespace FlowOS.Security.Policies;

public interface IPolicyEvaluator
{
    PolicyResult Evaluate(Policy policy, PolicyContext context);
}
