using FlowOS.Workflows.Domain;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowDefinitionTests
{
    [Fact]
    public void SetClassLineage_RepinningToDifferentClass_ThrowsPinConflict()
    {
        // Tenant integration finding H2: publishing a second WorkflowClass under a (Name, Version)
        // already pinned to a different WorkflowClass used to surface as a generic
        // InvalidOperationException, which the MCP layer could only report as MCP-INTERNAL. It now
        // throws a distinct type so callers can map it to its own error code (WF-NAME-PINNED).
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "ApprovalFlow");
        var firstClassId = Guid.NewGuid();
        definition.SetClassLineage(firstClassId, Guid.NewGuid());

        var secondClassId = Guid.NewGuid();
        var ex = Assert.Throws<WorkflowDefinitionPinConflictException>(
            () => definition.SetClassLineage(secondClassId, Guid.NewGuid()));

        Assert.Contains("WF-NAME-PINNED", ex.Message);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    [Fact]
    public void SetClassLineage_RepinningToSameClass_IsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "ApprovalFlow");
        var classId = Guid.NewGuid();
        var lawId = Guid.NewGuid();

        definition.SetClassLineage(classId, lawId);
        definition.SetClassLineage(classId, lawId); // repairs/no-ops rather than throwing

        Assert.Equal(classId, definition.SourceWorkflowClassId);
    }
}
