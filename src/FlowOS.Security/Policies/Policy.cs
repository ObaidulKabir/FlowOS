namespace FlowOS.Security.Policies;

public class Policy
{
    public string PolicyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    /// <summary>Opaque JSON rule payload from storage. Evaluated by <see cref="IPolicyEvaluator"/>.</summary>
    public string ConditionJson { get; set; } = "{}";

    public Policy() { }

    public Policy(string name, string scope, string description, string conditionJson = "{}")
    {
        Name = name;
        Scope = scope;
        Description = description;
        ConditionJson = conditionJson;
        PolicyId = Guid.NewGuid().ToString();
    }
}
