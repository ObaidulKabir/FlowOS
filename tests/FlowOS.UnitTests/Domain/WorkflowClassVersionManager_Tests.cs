using System;
using System.Collections.Generic;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;

namespace FlowOS.UnitTests.Domain;

public class WorkflowClassVersionManager_Tests
{
    private static WorkflowClassBlueprint CreateBlueprint()
    {
        return new WorkflowClassBlueprint
        {
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Start",
                States = new List<string> { "Start" }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Start",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint
                    {
                        StepId = "Start",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                    }
                }
            }
        };
    }

    private static WorkflowClass CreateWorkflowClass(Guid? tenantId = null, string version = "1.0.0")
    {
        return new WorkflowClass(tenantId ?? Guid.NewGuid(), "TestWorkflow", version, CreateBlueprint());
    }

    [Fact]
    public void CreateCopyForTenant_CreatesFreshDraftForTargetTenant()
    {
        var source = CreateWorkflowClass();
        var workflowClassManager = new WorkflowClassManager();
        workflowClassManager.Publish(source);
        workflowClassManager.SubmitForReview(source);
        workflowClassManager.ApproveAsPublic(source);
        var newTenantId = Guid.NewGuid();
        var manager = new WorkflowClassVersionManager();

        var copy = manager.CreateCopyForTenant(source, newTenantId);

        Assert.NotSame(source, copy);
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal(newTenantId, copy.TenantId);
        Assert.Equal(source.Name, copy.Name);
        Assert.Equal("1.0.0", copy.Version);
        Assert.Equal(source.Definition, copy.Definition);
        Assert.Equal(WorkflowClassStatus.Draft, copy.Status);
        Assert.Equal(WorkflowClassScope.Private, copy.Scope);
        Assert.Null(copy.PreviousVersionId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void CreateNewVersion_WhenVersionIsMissing_ThrowsArgumentNullException(string? newVersion)
    {
        var source = CreateWorkflowClass();
        var manager = new WorkflowClassVersionManager();

        Assert.Throws<ArgumentNullException>(() => manager.CreateNewVersion(source, newVersion!));
    }
}
