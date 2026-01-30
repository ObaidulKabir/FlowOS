namespace FlowOS.Security.Policies;

public class Policy
{
    public string PolicyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty; // Added Scope

    public Policy() { }

    public Policy(string name, string scope, string description)
    {
        Name = name;
        Scope = scope;
        Description = description;
        PolicyId = Guid.NewGuid().ToString();
    }
}
