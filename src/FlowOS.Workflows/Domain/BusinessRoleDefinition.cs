namespace FlowOS.Workflows.Domain;

/// <summary>
/// A business-context role declared by the source WorkflowClass, compiled onto this
/// WorkflowDefinition. Purely declarative — no membership is granted here, and nothing is written
/// to FlowOS's own Role/TenantUserRole tables. Membership is resolved fresh, per running instance,
/// only at the moment a step checks it — see <see cref="ResolutionType"/> and
/// <see cref="WorkflowInstance.RoleAssignments"/>.
/// </summary>
public class BusinessRoleDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = new();

    /// <summary>"Assignment" (default) | "Expression" | "Static". See RoleBlueprint.ResolutionType for what each means.</summary>
    public string ResolutionType { get; set; } = "Assignment";

    /// <summary>Expression resolution: a template (e.g. "{{ManagerEmail}}") evaluated against the instance's business payload.</summary>
    public string? MemberExpression { get; set; }

    /// <summary>Static resolution: fixed caller identifiers, after binding overrides.</summary>
    public List<string> StaticMembers { get; set; } = new();

    public BusinessRoleDefinition() { }
}
