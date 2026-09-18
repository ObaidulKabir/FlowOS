using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.API.Services;
using FlowOS.Application.Services;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class FlagshipWorkflowsSeederTests
{
    private readonly DbContextOptions<FlowOSDbContext> _dbOptions;
    private readonly Guid _testTenantId = Guid.NewGuid();

    public FlagshipWorkflowsSeederTests()
    {
        _dbOptions = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName: $"FlowOS_FlagshipTests_{Guid.NewGuid()}")
            .Options;
    }

    [Fact]
    public async Task DataSeeder_Seeds_AllThreeFlagshipWorkflows_WithPublicCatalogStatus()
    {
        // Arrange
        using var db = new FlowOSDbContext(_dbOptions);

        // Act
        await DataSeeder.SeedFlagshipWorkflowsAsync(db, _testTenantId);

        // Assert WorkflowClasses
        var sagaWc = await db.WorkflowClasses.FirstOrDefaultAsync(w => w.TenantId == _testTenantId && w.Name == "OrderSagaFulfillment");
        var loanWc = await db.WorkflowClasses.FirstOrDefaultAsync(w => w.TenantId == _testTenantId && w.Name == "LoanUnderwritingFlow");
        var secOpsWc = await db.WorkflowClasses.FirstOrDefaultAsync(w => w.TenantId == _testTenantId && w.Name == "SecOpsAccessGovernance");

        Assert.NotNull(sagaWc);
        Assert.NotNull(loanWc);
        Assert.NotNull(secOpsWc);

        Assert.Equal(WorkflowClassStatus.Public, sagaWc.Status);
        Assert.Equal(WorkflowClassScope.Public, sagaWc.Scope);

        Assert.Equal(WorkflowClassStatus.Public, loanWc.Status);
        Assert.Equal(WorkflowClassScope.Public, loanWc.Scope);

        Assert.Equal(WorkflowClassStatus.Public, secOpsWc.Status);
        Assert.Equal(WorkflowClassScope.Public, secOpsWc.Scope);

        // Assert WorkflowDefinitions
        var sagaDef = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.TenantId == _testTenantId && d.Name == "OrderSagaFulfillment");
        var loanDef = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.TenantId == _testTenantId && d.Name == "LoanUnderwritingFlow");
        var secOpsDef = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.TenantId == _testTenantId && d.Name == "SecOpsAccessGovernance");

        Assert.NotNull(sagaDef);
        Assert.NotNull(loanDef);
        Assert.NotNull(secOpsDef);

        Assert.Equal(WorkflowStatus.Published, sagaDef.Status);
        Assert.Equal(WorkflowStatus.Published, loanDef.Status);
        Assert.Equal(WorkflowStatus.Published, secOpsDef.Status);
    }

    [Fact]
    public async Task FlagshipWorkflows_Validate_Cleanly_With_WorkflowClassValidator()
    {
        // Arrange
        using var db = new FlowOSDbContext(_dbOptions);
        await DataSeeder.SeedFlagshipWorkflowsAsync(db, _testTenantId);
        var validator = new WorkflowClassValidator();

        var flagshipNames = new[] { "OrderSagaFulfillment", "LoanUnderwritingFlow", "SecOpsAccessGovernance" };

        foreach (var name in flagshipNames)
        {
            var wc = await db.WorkflowClasses.FirstAsync(w => w.TenantId == _testTenantId && w.Name == name);

            // Act
            var result = validator.Validate(wc);

            // Assert
            Assert.True(result.IsValid, $"Validation failed for {name}: {string.Join(", ", result.Errors.Select(e => e.Message))}");
            Assert.Empty(result.Errors);
        }
    }

    [Fact]
    public async Task OrderSagaFulfillment_Has_ExpectedCompensations_And_DynamicCalculations()
    {
        // Arrange
        using var db = new FlowOSDbContext(_dbOptions);
        await DataSeeder.SeedFlagshipWorkflowsAsync(db, _testTenantId);

        var wc = await db.WorkflowClasses.FirstAsync(w => w.TenantId == _testTenantId && w.Name == "OrderSagaFulfillment");
        var bp = wc.Definition;

        // Act & Assert
        Assert.Equal(5, bp.Workflow.Steps.Count);

        var authStep = bp.Workflow.Steps.First(s => s.StepId == "AuthorizePayment");
        Assert.Single(authStep.OnEntry);
        Assert.Equal("Webhook", authStep.OnEntry[0].ActionType);
        Assert.True(authStep.OnEntry[0].SignPayload);
        Assert.Equal("Amount * 1.05", authStep.OnEntry[0].PayloadMapping["taxedAmount"]);

        Assert.Single(authStep.OnFailure);
        Assert.Equal("Webhook", authStep.OnFailure[0].ActionType);
        Assert.Equal("https://api.stripe.com/v1/refunds/void-hold", authStep.OnFailure[0].Target);

        var reserveStep = bp.Workflow.Steps.First(s => s.StepId == "ReserveInventory");
        Assert.Equal(2, reserveStep.OnFailure.Count);
        Assert.Contains(reserveStep.OnFailure, a => a.ActionType == "Webhook" && a.Target.Contains("release-sku"));
        Assert.Contains(reserveStep.OnFailure, a => a.ActionType == "Notification" && a.Target == "WarehouseOps");
    }

    [Fact]
    public async Task LoanUnderwritingFlow_Has_DecisionRouting_And_HMACSignatures()
    {
        // Arrange
        using var db = new FlowOSDbContext(_dbOptions);
        await DataSeeder.SeedFlagshipWorkflowsAsync(db, _testTenantId);

        var wc = await db.WorkflowClasses.FirstAsync(w => w.TenantId == _testTenantId && w.Name == "LoanUnderwritingFlow");
        var bp = wc.Definition;

        // Act & Assert
        var evalStep = bp.Workflow.Steps.First(s => s.StepId == "EvaluateRisk");
        Assert.Equal("Decision", evalStep.StepType);
        Assert.True(evalStep.Conditions.ContainsKey("CreditScore >= 720 && DebtToIncome < 0.35"));
        Assert.True(evalStep.Conditions.ContainsKey("CreditScore < 580"));
        Assert.True(evalStep.Conditions.ContainsKey("Default"));

        var reviewStep = bp.Workflow.Steps.First(s => s.StepId == "UnderwriterReview");
        Assert.Equal("HumanTask", reviewStep.StepType);
        Assert.Equal("48h", reviewStep.Sla?.Duration);
        Assert.Equal("EVT-DECLINE", reviewStep.Sla?.TimeoutEvent);
        Assert.Contains("event.publish.EVT-FINAL-APPROVE", reviewStep.RequiredCapabilities);
        Assert.Contains("event.publish.EVT-DECLINE", reviewStep.RequiredCapabilities);
        Assert.Contains(bp.Roles, role =>
            role.Name == "Director" &&
            role.GrantedCapabilities.Contains("event.publish.EVT-FINAL-APPROVE"));

        var disburseStep = bp.Workflow.Steps.First(s => s.StepId == "DisburseFunds");
        var webhookAction = disburseStep.OnEntry.First(a => a.ActionType == "Webhook");
        Assert.True(webhookAction.SignPayload);
        Assert.Equal("https://core-banking.partner.com/api/v2/disbursements", webhookAction.Target);
    }

    [Fact]
    public async Task SecOpsAccessGovernance_Has_24hSlaEscalation_And_8hTimerRevocation()
    {
        // Arrange
        using var db = new FlowOSDbContext(_dbOptions);
        await DataSeeder.SeedFlagshipWorkflowsAsync(db, _testTenantId);

        var wc = await db.WorkflowClasses.FirstAsync(w => w.TenantId == _testTenantId && w.Name == "SecOpsAccessGovernance");
        var bp = wc.Definition;

        // Act & Assert
        var managerStep = bp.Workflow.Steps.First(s => s.StepId == "ManagerApproval");
        Assert.Equal("HumanTask", managerStep.StepType);
        Assert.Contains("event.publish.EVT-APPROVE", managerStep.RequiredCapabilities);
        Assert.Contains(bp.Roles, role =>
            role.Name == "Director" &&
            role.GrantedCapabilities.Contains("event.publish.EVT-APPROVE"));
        Assert.Equal("24h", managerStep.Sla?.Duration);
        Assert.Equal("EVT-ESCALATE", managerStep.Sla?.TimeoutEvent);
        Assert.Equal("DirectorEscalation", managerStep.Sla?.EscalationStepId);
        Assert.Equal("Director", managerStep.Sla?.EscalationRole);

        var timerStep = bp.Workflow.Steps.First(s => s.StepId == "SessionExpirationTimer");
        Assert.Equal("Timer", timerStep.StepType);
        Assert.Equal("8h", timerStep.Sla?.Duration);
        Assert.Equal("EVT-REVOKE", timerStep.Sla?.TimeoutEvent);

        var revokeStep = bp.Workflow.Steps.First(s => s.StepId == "RevokeAccess");
        Assert.Single(revokeStep.OnEntry, a => a.ActionType == "Webhook");
        Assert.Single(revokeStep.OnEntry, a => a.ActionType == "Notification");
    }
}
