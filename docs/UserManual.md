# FlowOS Developer Manual

**Version 1.0.0**
*Enterprise Process Operating System*

FlowOS is a kernel-style process engine designed for correctness, compliance, and enterprise scale. It strictly separates **Workflow Logic (State Machines)** from **Business Logic (Policy & Agents)**.

---

## 🚀 Quick Start

### 1. Define a Workflow
Workflows are defined in code or configuration. They consist of **Steps** and **Transitions**.

```csharp
var definition = new WorkflowDefinition(tenantId, "ExpenseApproval", 1);

// Step 1: Submission (Command)
definition.AddStep(new WorkflowStepDefinition("Submit", WorkflowStepType.Command)
{
    NextSteps = { { "Submitted", "ManagerReview" } }
});

// Step 2: Manager Review (Human Task)
definition.AddStep(new WorkflowStepDefinition("ManagerReview", WorkflowStepType.HumanTask)
{
    AllowedRoles = { "Manager" },
    NextSteps = { 
        { "Approved", "FinanceReview" },
        { "Rejected", "End" } 
    }
});

// Step 3: Finance Review (Human Task)
definition.AddStep(new WorkflowStepDefinition("FinanceReview", WorkflowStepType.HumanTask)
{
    AllowedRoles = { "Finance" },
    NextSteps = { { "Paid", "End" } }
});

definition.Publish();
```

### 2. Start an Instance
Use the API or Command Bus to launch a workflow.

```csharp
var command = new StartWorkflowCommand(
    TenantId: tenantId,
    WorkflowDefinitionId: definition.Id,
    Version: 1,
    InitialStepId: "Submit"
);

var instanceId = await _mediator.Send(command);
```

---

## 🤖 AI Agents (Advisory Only)

FlowOS treats AI Agents as **advisory components**. They cannot mutate state directly.

### Implementing an Agent
Implement `IAgent` to analyze context and return insights.

```csharp
public class RiskAnalysisAgent : IAgent
{
    public async Task<AgentResult> ExecuteAsync(AgentContext context)
    {
        // 1. Read safe context (Immutable)
        var entity = context.EntitySnapshot;
        
        // 2. Perform Analysis
        if (entity.Amount > 10000)
        {
            return AgentResult.FromInsight(
                "High risk transaction detected. Recommend rigorous audit.",
                new Dictionary<string, object> { { "risk_score", 0.95 } }
            );
        }

        return AgentResult.FromInsight("Low risk.");
    }
}
```

### Consuming Insights
Insights are projected into the `AgentInsightReadModel` and visible via the Admin/Task API.

```json
// GET /api/tasks/{id}
{
  "taskId": "...",
  "currentStep": "ManagerReview",
  "agentInsights": [
    {
      "agentId": "RiskAnalysisAgent",
      "insight": "High risk transaction detected.",
      "confidence": 0.95
    }
  ]
}
```

---

## 👮 Governance & Policy

Policies are **deny-only**. They run before any command reaches the engine.

### Defining a Policy
```csharp
public class WeekendFreezePolicy : IPolicyEvaluator
{
    public Task<PolicyResult> EvaluateAsync(PolicyContext context)
    {
        if (DateTime.UtcNow.DayOfWeek == DayOfWeek.Saturday || 
            DateTime.UtcNow.DayOfWeek == DayOfWeek.Sunday)
        {
            return Task.FromResult(PolicyResult.Deny("No approvals allowed on weekends."));
        }
        
        return Task.FromResult(PolicyResult.Allow());
    }
}
```

---

## 👤 Human Tasks

Human tasks rely on the **Task API**. The UI never advances the workflow directly; it emits `TaskCompleted` events.

### Completing a Task
```bash
POST /api/tasks/{taskId}/complete
Authorization: Bearer <user_token>
```

This emits a `TaskCompleted` event. The Engine then checks if the current step allows a transition on `TaskCompleted`.

---

## 👁️ Admin Visibility

Use the Admin API to inspect workflow state without editing it.

```bash
GET /api/admin/workflows/{instanceId}
```

**Response:**
```json
{
  "id": "...",
  "status": "Running",
  "currentStepId": "ManagerReview",
  "timeline": [
    {
      "timestamp": "2023-10-27T10:00:00Z",
      "eventType": "WorkflowStarted",
      "summary": "Workflow started by user X"
    },
    {
      "timestamp": "2023-10-27T10:00:05Z",
      "eventType": "AgentInsightGenerated",
      "summary": "Agent RiskAnalysisAgent suggested: High risk transaction"
    }
  ]
}
```

---

## 🏗 Architecture Principles

1.  **Engine First**: The State Machine Engine is the only authority on state.
2.  **Event Driven**: All mutations are side-effects of processed events.
3.  **Deny-Only Policy**: Policies prevent actions; they never execute them.
4.  **Advisory AI**: Agents provide insights; humans or strict rules make decisions.
5.  **CQRS**: Reads (UI) are decoupled from Writes (Engine).
