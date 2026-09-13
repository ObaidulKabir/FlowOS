using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Services;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowCopilotServiceTests
{
    private readonly WorkflowCopilotService _copilot;
    private readonly WorkflowClassValidator _validator;

    public WorkflowCopilotServiceTests()
    {
        _validator = new WorkflowClassValidator();
        _copilot = new WorkflowCopilotService(_validator);
    }

    [Fact]
    public async Task Synthesize_ParallelExecution_CreatesValidForkJoinBlueprint()
    {
        // Arrange
        var prompt = "Create an insurance claim workflow with parallel vehicle damage assessment and medical check, plus manager review.";

        // Act
        var result = await _copilot.GenerateBlueprintAsync(prompt);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Blueprint);
        Assert.True(result.Validation.IsValid, $"Validation failed: {string.Join("; ", result.Validation.Errors.Select(e => e.Message))}");
        
        var forkStep = result.Blueprint.Workflow.Steps.FirstOrDefault(s => s.StepType == "Fork");
        Assert.NotNull(forkStep);
        Assert.True(forkStep.Branches.Count >= 2);

        var joinStep = result.Blueprint.Workflow.Steps.FirstOrDefault(s => s.StepType == "Join");
        Assert.NotNull(joinStep);
        Assert.Equal("WaitAll", joinStep.JoinPolicy);
        Assert.True(joinStep.InboundSteps.Count >= 2);
    }

    [Fact]
    public async Task Synthesize_DecisionRules_CreatesDecisionStepWithConditions()
    {
        // Arrange
        var prompt = "Commercial loan underwriting process with risk score decision rules and threshold evaluation.";

        // Act
        var result = await _copilot.GenerateBlueprintAsync(prompt);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Validation.IsValid, $"Validation failed: {string.Join("; ", result.Validation.Errors.Select(e => e.Message))}");

        var decisionStep = result.Blueprint.Workflow.Steps.FirstOrDefault(s => s.StepType == "Decision");
        Assert.NotNull(decisionStep);
        Assert.NotEmpty(decisionStep.Conditions);
    }

    [Fact]
    public async Task Synthesize_SlaAndWebhook_ConfiguresSlaAndActionHooks()
    {
        // Arrange
        var prompt = "Vendor onboarding flow with 48h SLA timeout, webhook API notification, and manager approval.";

        // Act
        var result = await _copilot.GenerateBlueprintAsync(prompt);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Validation.IsValid, $"Validation failed: {string.Join("; ", result.Validation.Errors.Select(e => e.Message))}");

        var reviewStep = result.Blueprint.Workflow.Steps.FirstOrDefault(s => s.StepType == "HumanTask");
        Assert.NotNull(reviewStep);
        Assert.NotNull(reviewStep.Sla);
        Assert.Equal("48h", reviewStep.Sla.Duration);

        var allActions = result.Blueprint.Workflow.Steps.SelectMany(s => s.OnEntry.Concat(s.OnExit).Concat(s.OnFailure)).ToList();
        Assert.NotEmpty(allActions);
    }

    [Fact]
    public async Task Refine_ExistingBlueprint_AddsSlaAndCompensation()
    {
        // Arrange
        var initial = await _copilot.GenerateBlueprintAsync("Basic expense approval flow");
        var refinePrompt = "Add 24h SLA timeout and compensation rollback webhook on review step";

        // Act
        var refined = await _copilot.GenerateBlueprintAsync(refinePrompt, initial.Blueprint, mode: "refine");

        // Assert
        Assert.NotNull(refined);
        Assert.True(refined.Validation.IsValid, $"Validation failed: {string.Join("; ", refined.Validation.Errors.Select(e => e.Message))}");

        var reviewStep = refined.Blueprint.Workflow.Steps.FirstOrDefault(s => s.StepType == "HumanTask");
        Assert.NotNull(reviewStep);
        Assert.NotNull(reviewStep.Sla);
        Assert.Equal("24h", reviewStep.Sla.Duration);
        Assert.NotEmpty(reviewStep.OnFailure);
    }

    [Fact]
    public async Task Synthesize_EmptyPrompt_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _copilot.GenerateBlueprintAsync("   "));
    }
}
