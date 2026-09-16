namespace FlowOS.Workflows.Domain;

public class StepAutoCommitDefinition
{
    public double MinConfidence { get; set; } = 0.9;
    public List<string> AllowedEvents { get; set; } = new();
}
