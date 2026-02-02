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
