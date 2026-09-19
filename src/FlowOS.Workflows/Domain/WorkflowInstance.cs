using System;
using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Domain;

public class WorkflowInstance : IWorkflowInstance
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    // Optional CorrelationId to link to external business entities or processes (e.g., OrderId, UserId)
    public Guid? CorrelationId { get; private set; }
    // Immutable properties captured at creation time
    public Guid WorkflowDefinitionId { get; private set; }
    public Guid WorkflowClassId { get; private set; } // Link to Governance Entity
    public int WorkflowVersion { get; private set; }
    public Guid? ParentWorkflowInstanceId { get; private set; }
    public string? ParentStepId { get; private set; }
    public string CurrentStepId { get; private set; }
    public string? CurrentState { get; private set; }
    public WorkflowInstanceStatus Status { get; private set; }

    // Parallel Execution Tokens
    public List<string> ActiveStepIds { get; private set; } = new();
    public List<string> CompletedParallelStepIds { get; private set; } = new();

    /// <summary>
    /// Travel counts keyed by <c>fromStep|event|toStep</c> for repeatable paths.
    /// Orchestration state only — not business data.
    /// </summary>
    public Dictionary<string, int> PathTravelCounts { get; private set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Business-role assignments for this instance only, keyed by the role name declared on
    /// <see cref="WorkflowDefinition.BusinessRoles"/> (e.g. "Approver" -&gt; "alice@acme.com").
    /// This is the "Assignment" resolution strategy for a business-context role: it comes to life
    /// only once this instance runs and something assigns it (see <see cref="AssignRole"/>), lives
    /// only on this instance, and is unrelated to FlowOS's own tenant Role/TenantUserRole tables.
    /// </summary>
    public Dictionary<string, string> RoleAssignments { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    // Orchestration state only - not business data

    protected WorkflowInstance()
    {
        CurrentStepId = null!;
        ActiveStepIds = new List<string>();
        CompletedParallelStepIds = new List<string>();
        PathTravelCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        RoleAssignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public WorkflowInstance(
        Guid tenantId,
        Guid definitionId,
        Guid workflowClassId,
        int version,
        string initialStepId,
        Guid? correlationId = null,
        string? initialState = null,
        Guid? parentWorkflowInstanceId = null,
        string? parentStepId = null)
    {
        if (string.IsNullOrWhiteSpace(initialStepId))
            throw new ArgumentNullException(nameof(initialStepId));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        WorkflowDefinitionId = definitionId;
        WorkflowClassId = workflowClassId;
        WorkflowVersion = version;
        ParentWorkflowInstanceId = parentWorkflowInstanceId;
        ParentStepId = parentStepId;
        CurrentStepId = initialStepId;
        CurrentState = initialState ?? initialStepId;
        Status = WorkflowInstanceStatus.Running;
        CorrelationId = correlationId;
        CreatedAt = DateTime.UtcNow;
        ActiveStepIds = new List<string> { initialStepId };
        CompletedParallelStepIds = new List<string>();
        PathTravelCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        RoleAssignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public void AdvanceTo(string nextStepId)
    {
        if (Status == WorkflowInstanceStatus.Completed || Status == WorkflowInstanceStatus.Failed)
            throw new InvalidOperationException("Cannot advance a terminated workflow.");

        CurrentStepId = nextStepId;
        ActiveStepIds = new List<string> { nextStepId };
        Status = WorkflowInstanceStatus.Running;
    }

    public void ForkTo(IEnumerable<string> branchStepIds)
    {
        if (Status == WorkflowInstanceStatus.Completed || Status == WorkflowInstanceStatus.Failed)
            throw new InvalidOperationException("Cannot fork a terminated workflow.");

        var list = branchStepIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (list.Count < 2)
            throw new InvalidOperationException("A fork must have at least 2 target branches.");

        ActiveStepIds = list;
        CurrentStepId = string.Join(", ", list);
        Status = WorkflowInstanceStatus.Running;
    }

    public void CompleteBranch(string completedStepId, string? nextStepInBranch = null)
    {
        ActiveStepIds.Remove(completedStepId);
        if (!CompletedParallelStepIds.Contains(completedStepId))
        {
            CompletedParallelStepIds.Add(completedStepId);
        }

        if (!string.IsNullOrWhiteSpace(nextStepInBranch) && nextStepInBranch != "END")
        {
            if (!ActiveStepIds.Contains(nextStepInBranch))
            {
                ActiveStepIds.Add(nextStepInBranch);
            }
        }

        CurrentStepId = ActiveStepIds.Count > 0 ? string.Join(", ", ActiveStepIds) : (nextStepInBranch ?? completedStepId);
    }

    public void JoinTo(string continuationStepId)
    {
        if (Status == WorkflowInstanceStatus.Completed || Status == WorkflowInstanceStatus.Failed)
            throw new InvalidOperationException("Cannot advance a terminated workflow.");

        CompletedParallelStepIds.Clear();
        CurrentStepId = continuationStepId;
        ActiveStepIds = new List<string> { continuationStepId };
        Status = WorkflowInstanceStatus.Running;
    }

    public void SetCurrentState(string state)
    {
        CurrentState = state;
    }

    /// <summary>
    /// Records that <paramref name="callerRef"/> is acting as <paramref name="roleName"/> for this
    /// instance — e.g. from an AssignRole step action, or a "Claim" call from a task list. Scoped
    /// strictly to this instance; never written anywhere else.
    /// </summary>
    public void AssignRole(string roleName, string callerRef)
    {
        if (string.IsNullOrWhiteSpace(roleName)) throw new ArgumentException("RoleName is required.", nameof(roleName));
        if (string.IsNullOrWhiteSpace(callerRef)) throw new ArgumentException("CallerRef is required.", nameof(callerRef));

        RoleAssignments[roleName.Trim()] = callerRef.Trim();
    }

    public void UnassignRole(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return;
        RoleAssignments.Remove(roleName.Trim());
    }

    public int GetPathTravelCount(string edgeKey)
    {
        return PathTravelCounts.TryGetValue(edgeKey, out var count) ? count : 0;
    }

    public int RecordPathTravel(string edgeKey)
    {
        if (string.IsNullOrWhiteSpace(edgeKey))
            return 0;

        PathTravelCounts.TryGetValue(edgeKey, out var count);
        count++;
        PathTravelCounts[edgeKey] = count;
        return count;
    }

    public void Complete()
    {
        Status = WorkflowInstanceStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void Wait()
    {
        Status = WorkflowInstanceStatus.Waiting;
    }

    public WorkflowInstance CreateTransientCopy()
    {
        var copy = new WorkflowInstance(
            TenantId,
            WorkflowDefinitionId,
            WorkflowClassId,
            WorkflowVersion,
            CurrentStepId,
            CorrelationId,
            CurrentState,
            ParentWorkflowInstanceId,
            ParentStepId)
        {
            Id = Id,
            Status = Status,
            CreatedAt = CreatedAt,
            CompletedAt = CompletedAt,
            ActiveStepIds = new List<string>(ActiveStepIds),
            CompletedParallelStepIds = new List<string>(CompletedParallelStepIds),
            PathTravelCounts = new Dictionary<string, int>(PathTravelCounts, StringComparer.Ordinal),
            RoleAssignments = new Dictionary<string, string>(RoleAssignments, StringComparer.OrdinalIgnoreCase)
        };

        return copy;
    }
}
