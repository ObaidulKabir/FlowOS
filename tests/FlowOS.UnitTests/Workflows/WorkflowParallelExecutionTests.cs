using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Application.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Events.Models;
using FlowOS.StateMachines.Engine;
using FlowOS.StateMachines.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowParallelExecutionTests
{
    private readonly WorkflowClassValidator _validator = new();
    private readonly StateMachineEngine _smEngine = new();
    private readonly WorkflowEngine _engine;

    public WorkflowParallelExecutionTests()
    {
        _engine = new WorkflowEngine(_smEngine);
    }

    [Fact]
    public void Validator_ShouldReject_Fork_WithFewerThanTwoBranches()
    {
        // Arrange
        var bp = new WorkflowClassBlueprint
        {
            Events = new() { new() { EventId = "EVT-SUBMIT", Name = "Submit" } },
            StateMachine = new()
            {
                InitialState = "Draft",
                States = new() { "Draft", "Active" },
                Transitions = new() { new() { FromState = "Draft", ToState = "Active", EventId = "EVT-SUBMIT" } }
            },
            Workflow = new()
            {
                StartStepId = "Split",
                Steps = new()
                {
                    new StepBlueprint
                    {
                        StepId = "Split",
                        StepType = "Fork",
                        Branches = new() { "OnlyOneBranch" } // Invalid: requires at least 2
                    },
                    new StepBlueprint
                    {
                        StepId = "OnlyOneBranch",
                        StepType = "Command",
                        NextSteps = new() { { "Default", "END" } }
                    }
                }
            }
        };

        var wc = new WorkflowClass(Guid.NewGuid(), "BadForkFlow", "1.0.0", bp);

        // Act
        var result = _validator.Validate(wc);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-FORK-001");
    }

    [Fact]
    public void Validator_ShouldReject_Join_WithFewerThanTwoInboundSteps()
    {
        // Arrange
        var bp = new WorkflowClassBlueprint
        {
            Events = new() { new() { EventId = "EVT-SUBMIT", Name = "Submit" } },
            StateMachine = new()
            {
                InitialState = "Draft",
                States = new() { "Draft", "Active" },
                Transitions = new() { new() { FromState = "Draft", ToState = "Active", EventId = "EVT-SUBMIT" } }
            },
            Workflow = new()
            {
                StartStepId = "Merge",
                Steps = new()
                {
                    new StepBlueprint
                    {
                        StepId = "Merge",
                        StepType = "Join",
                        InboundSteps = new() { "BranchA" }, // Invalid: requires at least 2
                        NextSteps = new() { { "Default", "END" } }
                    },
                    new StepBlueprint
                    {
                        StepId = "BranchA",
                        StepType = "Command",
                        NextSteps = new() { { "Default", "Merge" } }
                    }
                }
            }
        };

        var wc = new WorkflowClass(Guid.NewGuid(), "BadJoinFlow", "1.0.0", bp);

        // Act
        var result = _validator.Validate(wc);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-JOIN-001");
    }

    [Fact]
    public void Validator_ShouldPass_ValidParallelForkJoinBlueprint()
    {
        // Arrange
        var bp = CreateValidForkJoinBlueprint();
        var wc = new WorkflowClass(Guid.NewGuid(), "ParallelVerificationFlow", "1.0.0", bp);

        // Act
        var result = _validator.Validate(wc);

        // Assert
        Assert.True(result.IsValid, $"Validation failed: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void WorkflowEngine_Fork_ShouldSpawn_ConcurrentActiveTokens()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var bp = CreateValidForkJoinBlueprint();
        var wc = new WorkflowClass(tenantId, "ParallelVerificationFlow", "1.0.0", bp);
        var def = WorkflowClassCompiler.MapToRuntimeDefinition(wc);

        var instance = new WorkflowInstance(tenantId, def.Id, wc.Id, 1, "StartStep");
        var context = new FlowOS.StateMachines.Models.ExecutionContext();
        var startEvent = new StandardEvent(tenantId, "EVT-START");

        // Act: StartStep -> ForkGateway
        var advanceResult = _engine.Advance(instance, def, startEvent, context);

        // Assert: ForkGateway spawned CheckCredit and CheckFraud concurrently
        Assert.True(advanceResult.Success);
        Assert.Equal(2, instance.ActiveStepIds.Count);
        Assert.Contains("CheckCredit", instance.ActiveStepIds);
        Assert.Contains("CheckFraud", instance.ActiveStepIds);
    }

    [Fact]
    public void WorkflowEngine_WaitAllJoin_ShouldWait_UntilAllBranchesArrive()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var bp = CreateValidForkJoinBlueprint();
        var wc = new WorkflowClass(tenantId, "ParallelVerificationFlow", "1.0.0", bp);
        var def = WorkflowClassCompiler.MapToRuntimeDefinition(wc);

        var instance = new WorkflowInstance(tenantId, def.Id, wc.Id, 1, "StartStep");
        var context = new FlowOS.StateMachines.Models.ExecutionContext();

        // 1. Advance to Fork
        _engine.Advance(instance, def, new StandardEvent(tenantId, "EVT-START"), context);
        Assert.Equal(2, instance.ActiveStepIds.Count);

        // 2. Branch 1 (CheckCredit) completes -> arrives at JoinGateway
        var creditPassedEvent = new StandardEvent(tenantId, "EVT-CREDIT-PASSED");
        var branch1Result = _engine.Advance(instance, def, creditPassedEvent, context);

        // Assert: Branch 1 completed, but instance is still waiting for Branch 2
        Assert.True(branch1Result.Success);
        Assert.Single(instance.CompletedParallelStepIds);
        Assert.Contains("CheckCredit", instance.CompletedParallelStepIds);
        Assert.Single(instance.ActiveStepIds);
        Assert.Contains("CheckFraud", instance.ActiveStepIds);
        Assert.Equal(WorkflowInstanceStatus.Waiting, instance.Status);

        // 3. Branch 2 (CheckFraud) completes -> arrives at JoinGateway
        var fraudPassedEvent = new StandardEvent(tenantId, "EVT-FRAUD-CLEARED");
        var branch2Result = _engine.Advance(instance, def, fraudPassedEvent, context);

        // Assert: Both branches arrived, Join converged, and instance advanced to FinalApproval!
        Assert.True(branch2Result.Success);
        Assert.Equal("FinalApproval", instance.CurrentStepId);
        Assert.Contains("FinalApproval", instance.ActiveStepIds);
        Assert.Empty(instance.CompletedParallelStepIds);
    }

    [Fact]
    public void WorkflowEngine_WaitAnyJoin_ShouldAdvance_OnFirstBranchArrival()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var bp = CreateValidForkJoinBlueprint(joinPolicy: "WaitAny");
        var wc = new WorkflowClass(tenantId, "FastAnyVerificationFlow", "1.0.0", bp);
        var def = WorkflowClassCompiler.MapToRuntimeDefinition(wc);

        var instance = new WorkflowInstance(tenantId, def.Id, wc.Id, 1, "StartStep");
        var context = new FlowOS.StateMachines.Models.ExecutionContext();

        // 1. Advance to Fork
        _engine.Advance(instance, def, new StandardEvent(tenantId, "EVT-START"), context);

        // 2. Branch 1 (CheckCredit) completes -> arrives at WaitAny Join
        var creditPassedEvent = new StandardEvent(tenantId, "EVT-CREDIT-PASSED");
        var branch1Result = _engine.Advance(instance, def, creditPassedEvent, context);

        // Assert: WaitAny immediately advances to FinalApproval without waiting for Branch 2!
        Assert.True(branch1Result.Success);
        Assert.Equal("FinalApproval", instance.CurrentStepId);
        Assert.Contains("FinalApproval", instance.ActiveStepIds);
    }

    private static WorkflowClassBlueprint CreateValidForkJoinBlueprint(string joinPolicy = "WaitAll")
    {
        return new WorkflowClassBlueprint
        {
            Events = new()
            {
                new() { EventId = "EVT-START", Name = "Start Workflow" },
                new() { EventId = "EVT-CREDIT-PASSED", Name = "Credit Check Passed" },
                new() { EventId = "EVT-FRAUD-CLEARED", Name = "Fraud Check Cleared" },
                new() { EventId = "EVT-FINAL-APPROVE", Name = "Final Approve" }
            },
            StateMachine = new()
            {
                InitialState = "Draft",
                States = new() { "Draft", "Evaluating", "Approved" },
                Transitions = new()
                {
                    new() { FromState = "Draft", ToState = "Evaluating", EventId = "EVT-START" },
                    new() { FromState = "Evaluating", ToState = "Approved", EventId = "EVT-FINAL-APPROVE" }
                }
            },
            Workflow = new()
            {
                StartStepId = "StartStep",
                Steps = new()
                {
                    new()
                    {
                        StepId = "StartStep",
                        StepType = "Command",
                        NextSteps = new() { { "EVT-START", "ForkGateway" } }
                    },
                    new()
                    {
                        StepId = "ForkGateway",
                        StepType = "Fork",
                        Branches = new() { "CheckCredit", "CheckFraud" }
                    },
                    new()
                    {
                        StepId = "CheckCredit",
                        StepType = "HumanTask",
                        NextSteps = new() { { "EVT-CREDIT-PASSED", "JoinGateway" } }
                    },
                    new()
                    {
                        StepId = "CheckFraud",
                        StepType = "HumanTask",
                        NextSteps = new() { { "EVT-FRAUD-CLEARED", "JoinGateway" } }
                    },
                    new()
                    {
                        StepId = "JoinGateway",
                        StepType = "Join",
                        JoinPolicy = joinPolicy,
                        InboundSteps = new() { "CheckCredit", "CheckFraud" },
                        NextSteps = new() { { "Default", "FinalApproval" } }
                    },
                    new()
                    {
                        StepId = "FinalApproval",
                        StepType = "Command",
                        NextSteps = new() { { "Default", "END" } }
                    }
                }
            }
        };
    }
}
