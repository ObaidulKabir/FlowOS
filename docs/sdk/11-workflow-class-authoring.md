# 11 - WorkflowClass Authoring

WorkflowClass is the robust packaging format for FlowOS logic. Unlike raw `WorkflowDefinitions`, a `WorkflowClass` bundles the Vocabulary (Events), Law (State Machine), and Orchestration (Workflow) into a single versioned unit.

## The Mental Model

Think of `WorkflowClass` as a **Class** in programming, and a running Workflow Instance as an **Object**.

* **WorkflowClass**: The blueprint (JSON/Builder).
* **Runtime Definition**: The compiled executable (what the engine runs).

## Creating a WorkflowClass

You define a `WorkflowClassBlueprint` containing all necessary components.

```csharp
var blueprint = new WorkflowClassBlueprint
{
    Events = { ... },
    StateMachine = { ... },
    Workflow = { ... },
    Roles = { ... }
};

var workflowClass = new WorkflowClass(tenantId, "MyProcess", "1.0.0", blueprint);
```

```
public record StepBlueprint
{
    public string StepId { get; init; } = string.Empty;
    public string StepType { get; init; } = "Command"; // Enum mapped to string for blueprint
    public Dictionary<string, string> NextSteps { get; init; } = new();
    public Dictionary<string, string> Conditions { get; init; } = new(); // Added: Decision Logic
    public List<string> RequiredRoles { get; init; } = new();
}
```

## Governance Flow

1. **Draft**: You edit the blueprint.
2. **Publish**: The system validates integrity (Consistency, Law, Governance). If valid, it becomes **Private/Published**.
3. **Deploy**: You compile the Published class into the Runtime Engine to start instances.

## Sharing

To share logic with other tenants (or the marketplace):

1. **Submit**: Move from Private to **Shared**.
2. **Review**: Admins validate safety.
3. **Approve**: Becomes **Public**.
4. **Adopt**: Other tenants **Copy** the Public class into their Private scope to use it.
