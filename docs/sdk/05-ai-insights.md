# 05 - AI Insights

AI Agents can participate in workflows by observing events and publishing insights.

*Note: While not explicitly covered in the Design Consultancy E2E tests yet, the mechanism is identical to human events.*

## Scenario
An AI agent analyzes the design and publishes a risk assessment.

## Code Example
*Conceptual Model*

```csharp
var insightCommand = new PublishAgentInsightCommand(
    tenantId,
    workflowId,
    "RiskBot-01",
    "High Risk detected in design.",
    "Risk Assessment"
);

await client.PostAsJsonAsync("/api/agents/insights", insightCommand);
```

## What Happened?
1. **Insight Recorded**: The insight is saved.
2. **Notification**: A `EVT-AGENT-INSIGHT` event is published.
3. **Reaction**: The workflow (if listening) or Notification Service (if configured) reacts.
   - For example, a high-risk insight could trigger a policy that blocks approval.

## Agent Suggestions
Beyond insights, Agents can now propose **Suggested Actions** to advance the workflow.

### Example: Risk Analyzer
An agent analyzes expense data and suggests `EVT-ESCALATE` if the amount exceeds a threshold.

**Agent Implementation (`IWorkflowAgent`):**
```csharp
public class RiskAnalysisAgent : IWorkflowAgent
{
    public Task<AgentResult> ExecuteAsync(AgentContext context)
    {
        var expense = context.EntitySnapshot as Dictionary<string, object>;
        if ((double)expense["Amount"] > 5000)
        {
            return AgentResult.WithActions("High Risk", new List<SuggestedAction>
            {
                new SuggestedAction("EVT-ESCALATE", "Amount > $5k", 0.95)
            });
        }
        return AgentResult.FromInsight("No risk detected");
    }
}
```

These suggestions appear in the UI for human confirmation. For integration details, see [Agent Integration Strategy](../AgentIntegrationStrategy.md).
