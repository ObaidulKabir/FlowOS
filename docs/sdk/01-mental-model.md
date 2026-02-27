# 01 - Mental Model

## "Clients React. FlowOS Decides."

The core philosophy of FlowOS is that your client application (UI, API consumer, or external service) never decides the next state of a workflow. You only express **intent** through commands or events.

### The Cycle

1. **Intent**: You tell FlowOS what happened (e.g., "Task Completed", "Design Approved").
2. **Decision**: FlowOS evaluates the Workflow Definition, State Machine rules, and active Policies.
3. **Transition**: If valid, FlowOS advances the workflow to the next step.
4. **Reaction**: You query the new state or listen for notifications to update your UI.

### Key Concepts

- **Workflow**: A sequence of steps (e.g., "DesignConsultancy").
- **Instance**: A running execution of a workflow.
- **Step**: A specific point in the workflow (e.g., "DesignTask", "Review").
- **Event**: A signal that something occurred (e.g., `EVT-DESIGN-APPROVED`).
- **Policy**: A rule that can block an action based on context (e.g., "Weekend Freeze").

### Example: Design Consultancy

In the following chapters, we will build and use the **Design Consultancy** workflow:

1. **Start** -> **DesignTask** (Designer works)
2. **DesignTask** -> **Review** (Manager reviews)
3. **Review** -> **End** (Approved) OR **Rejected**

You will see how to drive this flow using the SDK/API.
