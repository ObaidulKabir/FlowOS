using System;
using System.Collections.Generic;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;

namespace FlowOS.UnitTests.Domain;

public class WorkflowClassManager_Tests
{
    private static WorkflowClass CreateValidDraft()
    {
        var blueprint = new WorkflowClassBlueprint
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

        return new WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", blueprint);
    }

    [Fact]
    public void ValidateOnly_InvalidWorkflow_ReturnsValidationErrors()
    {
        var invalidBlueprint = new WorkflowClassBlueprint
        {
            Workflow = new WorkflowBlueprint
            {
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint
                    {
                        StepId = "",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string>()
                    }
                }
            }
        };

        var workflowClass = new WorkflowClass(Guid.NewGuid(), "InvalidWorkflow", "1.0.0", invalidBlueprint);
        var manager = new WorkflowClassManager();

        var result = manager.ValidateOnly(workflowClass);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void CreateDraft_FromPublishedState_ReturnsStateError()
    {
        var workflowClass = CreateValidDraft();
        var manager = new WorkflowClassManager();
        manager.Publish(workflowClass);

        var result = manager.CreateDraft(workflowClass);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "STATE");
    }

    [Fact]
    public void SubmitForReview_FromPublished_MarksWorkflowAsShared()
    {
        var workflowClass = CreateValidDraft();
        var manager = new WorkflowClassManager();
        manager.Publish(workflowClass);

        var result = manager.SubmitForReview(workflowClass);

        Assert.True(result.IsValid);
        Assert.Equal(WorkflowClassStatus.Shared, workflowClass.Status);
        Assert.Equal(WorkflowClassScope.Shared, workflowClass.Scope);
    }

    [Fact]
    public void SubmitForReview_FromDraft_ReturnsStateError()
    {
        var workflowClass = CreateValidDraft();
        var manager = new WorkflowClassManager();

        var result = manager.SubmitForReview(workflowClass);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "STATE");
    }

    [Fact]
    public void WithdrawSubmission_FromShared_ReturnsWorkflowToPublishedPrivate()
    {
        var workflowClass = CreateValidDraft();
        var manager = new WorkflowClassManager();
        manager.Publish(workflowClass);
        manager.SubmitForReview(workflowClass);

        var result = manager.WithdrawSubmission(workflowClass);

        Assert.True(result.IsValid);
        Assert.Equal(WorkflowClassStatus.Published, workflowClass.Status);
        Assert.Equal(WorkflowClassScope.Private, workflowClass.Scope);
    }

    [Fact]
    public void ApproveAsPublic_FromShared_MarksWorkflowAsPublic()
    {
        var workflowClass = CreateValidDraft();
        var manager = new WorkflowClassManager();
        manager.Publish(workflowClass);
        manager.SubmitForReview(workflowClass);

        var result = manager.ApproveAsPublic(workflowClass);

        Assert.True(result.IsValid);
        Assert.Equal(WorkflowClassStatus.Public, workflowClass.Status);
        Assert.Equal(WorkflowClassScope.Public, workflowClass.Scope);
    }

    [Fact]
    public void ApproveAsPublic_FromPublished_ReturnsStateError()
    {
        var workflowClass = CreateValidDraft();
        var manager = new WorkflowClassManager();
        manager.Publish(workflowClass);

        var result = manager.ApproveAsPublic(workflowClass);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "STATE");
    }

    [Fact]
    public void Deprecate_FromPublished_MarksWorkflowAsDeprecated()
    {
        var workflowClass = CreateValidDraft();
        var manager = new WorkflowClassManager();
        manager.Publish(workflowClass);

        var result = manager.Deprecate(workflowClass);

        Assert.True(result.IsValid);
        Assert.Equal(WorkflowClassStatus.Deprecated, workflowClass.Status);
    }

    [Fact]
    public void Deprecate_WhenAlreadyDeprecated_IsIdempotent()
    {
        var workflowClass = CreateValidDraft();
        var manager = new WorkflowClassManager();
        manager.Publish(workflowClass);
        manager.Deprecate(workflowClass);

        var result = manager.Deprecate(workflowClass);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(WorkflowClassStatus.Deprecated, workflowClass.Status);
    }
}
