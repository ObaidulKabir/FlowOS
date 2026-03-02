# WorkflowClass Definition

The `WorkflowClass` is the core governance entity in FlowOS. It serves as the immutable template (or "Class") from which individual `WorkflowInstance`s (or "Objects") are created. It encapsulates the **Law** (State Machine), the **Work** (Orchestration), and the **Vocabulary** (Events) of a business process.

## Structure

A `WorkflowClass` is defined by a `WorkflowClassBlueprint`, which contains four main sections:

1.  **Events**: The vocabulary of signals that drive the process.
2.  **StateMachine**: The rules governing valid states and transitions.
3.  **Workflow**: The sequence of steps and tasks to be performed.
4.  **Roles & Capabilities**: The security model defining who can do what.

---

## 1. Events (Vocabulary & Data)

Events are the only way to advance a workflow. They represent significant business occurrences and can carry structured data (Payloads).

| Property | Type | Description |
| :--- | :--- | :--- |
| `EventId` | String | Unique identifier (e.g., `EVT-SUBMIT`). |
| `Name` | String | Human-readable name. |
| `Description` | String | Documentation of intent. |
| `Category` | Enum | `System`, `Human`, `Decision`, `Agent`. |
| `IsTerminal` | Boolean | Whether this event concludes the process. |
| `PayloadSchema` | JSON Schema | (Optional) Structure validation for event data. |

### Event Broadcasting
FlowOS uses a decoupled **NotificationStreamService** to broadcast projected events to connected clients in real-time via Server-Sent Events (SSE). This ensures users and systems are instantly aware of state changes without polling.

---

## 2. StateMachine (The Law)

The State Machine defines the legal states an entity can be in and the allowed transitions. It enforces invariants regardless of the workflow path.

| Property | Type | Description |
| :--- | :--- | :--- |
| `EntityType` | String | The domain entity being modeled (e.g., `Expense`). |
| `InitialState` | String | The starting state (e.g., `Draft`). |
| `States` | List<String> | All possible states. |
| `Transitions` | List<Transition> | Allowed state changes. |

**Transition Definition:**
- `FromState`: Current state.
- `ToState`: Target state.
- `EventId`: The event that triggers this transition.

---

## 3. Workflow (The Work)

The Workflow defines the orchestration logic—the sequence of steps, tasks, and decisions.

| Property | Type | Description |
| :--- | :--- | :--- |
| `StartStepId` | String | The entry point step. |
| `Steps` | List<Step> | The collection of steps. |

**Step Definition:**
| Property | Type | Description |
| :--- | :--- | :--- |
| `StepId` | String | Unique step identifier. |
| `StepType` | Enum | `Command`, `HumanTask`, `Timer`, `Decision`, `End`. |
| `NextSteps` | Dictionary | Map of `EventId` -> `NextStepId`. |
| `RequiredRoles` | List<String> | Roles allowed to perform this step. |
| `Conditions` | Dictionary | Logical conditions for automated routing. |

### Advanced Capabilities

#### Automatic Escalation
Workflows support automatic progression ("Auto-Advance") for steps that do not require human intervention. If a step's logic completes (e.g., a system check passes), the engine automatically triggers the "Default" transition to the next step. True escalation (e.g., overdue tasks) is handled via explicit `EVT-TASK-OVERDUE` events rather than implicit timers.

#### Human Action
Steps of type `HumanTask` explicitly pause the workflow execution (`Status: Waiting`) until a user with the required Role performs an action (emits an event).

#### AI Reasoning
FlowOS integrates AI Agents as advisory participants.
- **AgentInsight**: Agents analyze context and emit `EVT-AGENT-INSIGHT` events.
- **Read-Only**: Agents provide reasoning but cannot directly execute state changes unless explicitly authorized.
- **Integration**: Insights are projected into the workflow history and can be used by human decision-makers.

---

## 4. Roles & Capabilities (Governance)

Defines the security model. Access to trigger events is strictly controlled by Capabilities.

| Property | Type | Description |
| :--- | :--- | :--- |
| `Name` | String | Role name (e.g., `Manager`). |
| `GrantedCapabilities` | List<String> | specific permissions (e.g., `event.publish.EVT-APPROVE`). |

---

## JSON Example 1: Basic Expense Approval

```json
{
  "events": [
    { "eventId": "EVT-SUBMIT", "name": "Submit", "category": "Human" },
    { 
      "eventId": "EVT-APPROVE", 
      "name": "Approve", 
      "category": "Human",
      "payloadSchema": "{ \"type\": \"object\", \"properties\": { \"comment\": { \"type\": \"string\" } } }"
    },
    { "eventId": "EVT-AGENT-INSIGHT", "name": "AI Insight", "category": "Agent" }
  ],
  "stateMachine": {
    "initialState": "Draft",
    "states": ["Draft", "Pending", "Approved"],
    "transitions": [
      { "fromState": "Draft", "toState": "Pending", "eventId": "EVT-SUBMIT" },
      { "fromState": "Pending", "toState": "Approved", "eventId": "EVT-APPROVE" }
    ]
  },
  "workflow": {
    "startStepId": "Draft",
    "steps": [
      {
        "stepId": "Draft",
        "stepType": "Command",
        "nextSteps": { "EVT-SUBMIT": "Pending" }
      },
      {
        "stepId": "Pending",
        "stepType": "HumanTask",
        "requiredRoles": ["Manager"],
        "nextSteps": { "EVT-APPROVE": "Approved" }
      },
      {
        "stepId": "CheckFraud",
        "stepType": "Decision",
        "conditions": {
          "Payload.RiskScore > 0.8": "Flagged"
        },
        "nextSteps": { "Default": "Approved" }
      }
    ]
  }
}
```

## JSON Example 2: Complex Order Processing

This example demonstrates a multi-stage workflow with automated fraud checks, conditional routing based on order value, human approval for high-value orders, and AI-assisted review.

```json
{
  "events": [
    { "eventId": "EVT-ORDER-PLACED", "name": "Order Placed", "category": "System" },
    { "eventId": "EVT-FRAUD-CHECK-PASSED", "name": "Fraud Check Passed", "category": "System" },
    { "eventId": "EVT-FRAUD-CHECK-FAILED", "name": "Fraud Check Failed", "category": "System" },
    { "eventId": "EVT-AI-RISK-ANALYSIS", "name": "AI Risk Analysis", "category": "Agent", "payloadSchema": "{\"riskScore\": \"number\", \"reasoning\": \"string\"}" },
    { "eventId": "EVT-APPROVE-HIGH-VALUE", "name": "Approve High Value", "category": "Human" },
    { "eventId": "EVT-REJECT-ORDER", "name": "Reject Order", "category": "Human" },
    { "eventId": "EVT-SHIP-ORDER", "name": "Ship Order", "category": "System" }
  ],
  "stateMachine": {
    "initialState": "New",
    "states": ["New", "FraudCheck", "ManualReview", "Approved", "Rejected", "Shipped"],
    "transitions": [
      { "fromState": "New", "toState": "FraudCheck", "eventId": "EVT-ORDER-PLACED" },
      { "fromState": "FraudCheck", "toState": "Approved", "eventId": "EVT-FRAUD-CHECK-PASSED" },
      { "fromState": "FraudCheck", "toState": "ManualReview", "eventId": "EVT-FRAUD-CHECK-FAILED" },
      { "fromState": "ManualReview", "toState": "Approved", "eventId": "EVT-APPROVE-HIGH-VALUE" },
      { "fromState": "ManualReview", "toState": "Rejected", "eventId": "EVT-REJECT-ORDER" },
      { "fromState": "Approved", "toState": "Shipped", "eventId": "EVT-SHIP-ORDER" }
    ]
  },
  "workflow": {
    "startStepId": "New",
    "steps": [
      {
        "stepId": "New",
        "stepType": "Command",
        "nextSteps": { "EVT-ORDER-PLACED": "FraudAnalysis" }
      },
      {
        "stepId": "FraudAnalysis",
        "stepType": "Decision",
        "description": "Automated system check for fraud patterns",
        "conditions": {
          "Payload.FraudScore < 50": "CheckValue",
          "Payload.FraudScore >= 50": "AIManualReview"
        }
      },
      {
        "stepId": "CheckValue",
        "stepType": "Decision",
        "description": "Route based on order total",
        "conditions": {
          "Payload.TotalAmount > 5000": "HighValueApproval",
          "Payload.TotalAmount <= 5000": "Fulfillment"
        }
      },
      {
        "stepId": "AIManualReview",
        "stepType": "HumanTask",
        "description": "Human review required due to high fraud score. AI agent provides insight.",
        "requiredRoles": ["RiskOfficer"],
        "nextSteps": {
          "EVT-APPROVE-HIGH-VALUE": "Fulfillment",
          "EVT-REJECT-ORDER": "OrderRejected"
        }
      },
      {
        "stepId": "HighValueApproval",
        "stepType": "HumanTask",
        "description": "Director approval required for orders > $5000",
        "requiredRoles": ["Director"],
        "nextSteps": {
          "EVT-APPROVE-HIGH-VALUE": "Fulfillment",
          "EVT-REJECT-ORDER": "OrderRejected"
        }
      },
      {
        "stepId": "Fulfillment",
        "stepType": "SystemTask",
        "description": "Trigger shipping process",
        "nextSteps": { "EVT-SHIP-ORDER": "OrderShipped" }
      },
      {
        "stepId": "OrderShipped",
        "stepType": "End"
      },
      {
        "stepId": "OrderRejected",
        "stepType": "End"
      }
    ]
  },
  "roles": [
    {
      "name": "RiskOfficer",
      "grantedCapabilities": ["event.publish.EVT-APPROVE-HIGH-VALUE", "event.publish.EVT-REJECT-ORDER"]
    },
    {
      "name": "Director",
      "grantedCapabilities": ["event.publish.EVT-APPROVE-HIGH-VALUE", "event.publish.EVT-REJECT-ORDER"]
    }
  ]
}
```
