using FlowOS.API.Services;
using FlowOS.Application.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Tools;
using FlowOS.Workflows.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json.Linq;

namespace FlowOS.UnitTests.Workflows;

public class SandboxSampleSimulationTests
{
    private readonly SimulationTools _tools = new(new Mock<IMediator>().Object);

    [Fact]
    public async Task All_seeded_sandbox_samples_complete_happy_path_simulation()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"sandbox-sim-{tenantId}")
            .Options);

        SeedExpenseSamples(db, tenantId);
        await DataSeeder.SeedFlagshipWorkflowsAsync(db, tenantId);

        var cases = new (string Name, JObject Payload, string Role, JArray Events, bool AutoAdvanceTimers, string FinalState)[]
        {
            ("ExpenseApproval", new JObject { ["Amount"] = 50 }, "Approver",
                new JArray("EVT-SUBMIT", "EVT-APPROVE"), false, "Approved"),
            ("ExpenseApprovalV2", new JObject { ["Amount"] = 50 }, "Manager",
                new JArray("EVT-SUBMIT", "EVT-APPROVE"), false, "Approved"),
            ("ExpenseApprovalV2", new JObject { ["Amount"] = 7500 }, "Director",
                new JArray("EVT-SUBMIT", "EVT-DIRECTOR-APPROVE"), false, "Approved"),
            ("OrderSagaFulfillment", new JObject
            {
                ["OrderId"] = "ORD-1",
                ["Amount"] = 120,
                ["ItemSku"] = "SKU-9",
                ["Quantity"] = 2
            }, "User", new JArray("EVT-PAY-SUCCESS", "EVT-STOCK-LOCKED"), false, "InventoryReserved"),
            ("LoanUnderwritingFlow", new JObject
            {
                ["ApplicantName"] = "Ada",
                ["Amount"] = 20000,
                ["CreditScore"] = 750,
                ["DebtToIncome"] = 0.2,
                ["AccountNum"] = "ACC-1"
            }, "User", new JArray("EVT-APPLY", "EVT-AUTO-APPROVE"), false, "Approved"),
            ("LoanUnderwritingFlow", new JObject
            {
                ["ApplicantName"] = "Ben",
                ["Amount"] = 8000,
                ["CreditScore"] = 640,
                ["DebtToIncome"] = 0.4,
                ["AccountNum"] = "ACC-2"
            }, "Manager", new JArray("EVT-APPLY", "EVT-MANUAL-REVIEW", "EVT-FINAL-APPROVE"), false, "Approved"),
            ("SecOpsAccessGovernance", new JObject
            {
                ["UserEmail"] = "ops@example.com",
                ["Environment"] = "prod"
            }, "Manager", new JArray("EVT-REQUEST-ACCESS", "EVT-APPROVE"), true, "Revoked"),
            ("IncidentAlertEscalation", new JObject
            {
                ["TicketId"] = "INC-9",
                ["Severity"] = "Medium",
                ["Summary"] = "Checkout latency"
            }, "Support", new JArray("EVT-OPEN", "EVT-RESOLVE"), false, "Resolved"),
            ("IncidentAlertEscalation", new JObject
            {
                ["TicketId"] = "INC-1",
                ["Severity"] = "Critical",
                ["Summary"] = "Payments down"
            }, "OnCall", new JArray("EVT-OPEN", "EVT-CLOSE"), false, "Resolved"),
            ("QuoteAutoReview", new JObject
            {
                ["QuoteId"] = "Q-100",
                ["Amount"] = 900,
                ["Estimate"] = 900
            }, "Submitter", new JArray("EVT-SUBMIT"), false, "Accepted")
        };

        foreach (var (name, payload, role, events, autoAdvance, expectedState) in cases)
        {
            var workflowClass = await db.WorkflowClasses.FirstAsync(w => w.TenantId == tenantId && w.Name == name);
            var result = await Simulate(workflowClass.Definition, name, payload, role, events, autoAdvance);
            Assert.True(result.Ok, $"{name} simulation failed: {result.Error}");
            Assert.True(result.Status == "Completed", $"{name} status {result.Status} at {result.CurrentStepId}");
            Assert.Equal("END", result.CurrentStepId);
            Assert.Equal(expectedState, result.FinalState);
        }
    }

    [Fact]
    public async Task ExpenseApproval_reject_and_OrderSaga_compensate_complete()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"sandbox-alt-{tenantId}")
            .Options);

        SeedExpenseSamples(db, tenantId);
        await DataSeeder.SeedFlagshipWorkflowsAsync(db, tenantId);

        var expense = await db.WorkflowClasses.FirstAsync(w => w.Name == "ExpenseApproval");
        var reject = await Simulate(
            expense.Definition,
            expense.Name,
            new JObject { ["Amount"] = 20 },
            "Approver",
            new JArray("EVT-SUBMIT", "EVT-REJECT"),
            false);
        Assert.True(reject.Ok, reject.Error);
        Assert.Equal("Completed", reject.Status);
        Assert.Equal("Rejected", reject.FinalState);

        var saga = await db.WorkflowClasses.FirstAsync(w => w.Name == "OrderSagaFulfillment");
        var compensate = await Simulate(
            saga.Definition,
            saga.Name,
            new JObject { ["OrderId"] = "ORD-2", ["Amount"] = 90, ["ItemSku"] = "SKU-1", ["Quantity"] = 1 },
            "User",
            new JArray("EVT-PAY-SUCCESS", "EVT-STOCK-LOCKED", "EVT-SHIP-FAIL", "EVT-COMPENSATE"),
            false);
        Assert.True(compensate.Ok, compensate.Error);
        Assert.Equal("Completed", compensate.Status);
        Assert.Equal("RolledBack", compensate.FinalState);

        var expenseV2 = await db.WorkflowClasses.FirstAsync(w => w.Name == "ExpenseApprovalV2");
        var escalate = await Simulate(
            expenseV2.Definition,
            expenseV2.Name,
            new JObject { ["Amount"] = 250 },
            "Manager",
            new JArray("EVT-SUBMIT", "EVT-ESCALATE"),
            false);
        Assert.True(escalate.Ok, escalate.Error);
        Assert.Equal("WaitingForHumanTask", escalate.Status);
        Assert.Equal("PendingDirector", escalate.CurrentStepId);

        var directorCanEscalateManagerInbox = await Simulate(
            expenseV2.Definition,
            expenseV2.Name,
            new JObject { ["Amount"] = 250 },
            "Director",
            new JArray("EVT-SUBMIT", "EVT-ESCALATE"),
            false);
        Assert.True(directorCanEscalateManagerInbox.Ok, directorCanEscalateManagerInbox.Error);
        Assert.Equal("WaitingForHumanTask", directorCanEscalateManagerInbox.Status);
        Assert.Equal("PendingDirector", directorCanEscalateManagerInbox.CurrentStepId);
    }

    [Fact]
    public async Task Seeded_samples_compile_declarative_roles_onto_runtime_definitions()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"sandbox-roles-{tenantId}")
            .Options);

        SeedExpenseSamples(db, tenantId);
        await DataSeeder.SeedFlagshipWorkflowsAsync(db, tenantId);

        var expense = await db.WorkflowClasses.FirstAsync(w => w.Name == "ExpenseApproval");
        var expenseDef = WorkflowClassCompiler.MapToRuntimeDefinition(expense);
        Assert.Contains(expenseDef.BusinessRoles, role => role.Name == "Approver" && role.Capabilities.Contains("event.publish.EVT-APPROVE"));
        Assert.Contains(expenseDef.Steps.Single(s => s.StepId == "Pending").AllowedRoles, role => role == "Approver");

        var v2 = await db.WorkflowClasses.FirstAsync(w => w.Name == "ExpenseApprovalV2");
        var v2Def = WorkflowClassCompiler.MapToRuntimeDefinition(v2);
        Assert.Contains(v2Def.BusinessRoles, role => role.Name == "Director");
        Assert.Contains(v2Def.Steps.Single(s => s.StepId == "PendingDirector").AllowedRoles, role => role == "Director");
        var checkAmount = v2.Definition.Workflow.Steps.Single(step => step.StepId == "CheckAmount");
        Assert.Equal("Decision", checkAmount.StepType);
        Assert.Equal("PendingDirector", checkAmount.Conditions["Amount > 5000"]);
        Assert.Equal("CheckAmount", v2.Definition.Workflow.Steps.Single(step => step.StepId == "Draft").NextSteps["EVT-SUBMIT"]);

        var loan = await db.WorkflowClasses.FirstAsync(w => w.Name == "LoanUnderwritingFlow");
        var loanDef = WorkflowClassCompiler.MapToRuntimeDefinition(loan);
        Assert.Contains(loanDef.BusinessRoles, role => role.Name == "Manager" && role.Capabilities.Contains("event.publish.EVT-FINAL-APPROVE"));
        Assert.Contains(loan.Definition.Workflow.Steps.Single(s => s.StepId == "UnderwriterReview").RequiredRoles, role => role == "Manager");

        var incident = await db.WorkflowClasses.FirstAsync(w => w.Name == "IncidentAlertEscalation");
        var incidentDef = WorkflowClassCompiler.MapToRuntimeDefinition(incident);
        Assert.Contains(incidentDef.BusinessRoles, role => role.Name == "OnCall" && role.Capabilities.Contains("event.publish.EVT-CLOSE"));
        Assert.Contains(incident.Definition.Workflow.Steps.Single(s => s.StepId == "L1Review").RequiredRoles, role => role == "Support");
        Assert.Equal("EVT-ESCALATE", incident.Definition.Workflow.Steps.Single(s => s.StepId == "L1Review").Sla?.TimeoutEvent);

        var quote = await db.WorkflowClasses.FirstAsync(w => w.Name == "QuoteAutoReview");
        var quoteDef = WorkflowClassCompiler.MapToRuntimeDefinition(quote);
        Assert.Contains(quoteDef.BusinessRoles, role => role.Name == "QuoteAgent" && role.Capabilities.Contains("event.publish.EVT-ACCEPT"));
        var agentReview = quote.Definition.Workflow.Steps.Single(step => step.StepId == "AgentReview");
        Assert.Equal("Agent", agentReview.Actor);
        Assert.Contains("EVT-ACCEPT", agentReview.AutoCommit!.AllowedEvents);
        Assert.Equal("AdvisorReview", quote.Definition.Workflow.Steps.Single(step => step.StepId == "CheckAmount").Conditions["Amount > 1500"]);
    }

    [Fact]
    public async Task RepairExpenseApprovalV2_RestoresAmountRoutedRoles()
    {
        var tenantId = Guid.NewGuid();
        await using var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"sandbox-v2-repair-{tenantId}")
            .Options);

        var broken = new WorkflowClassBlueprint
        {
            Events =
            [
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request" },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request" },
                new EventBlueprint { EventId = "EVT-REJECT", Name = "Reject Request" },
                new EventBlueprint { EventId = "EVT-ESCALATE", Name = "Escalate to Director" },
                new EventBlueprint { EventId = "EVT-DIRECTOR-APPROVE", Name = "Director Approve" },
                new EventBlueprint { EventId = "EVT-DIRECTOR-REJECT", Name = "Director Reject" }
            ],
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = ["Draft", "PendingManager", "PendingDirector", "Approved", "Rejected"],
                Transitions =
                [
                    new TransitionBlueprint { FromState = "Draft", ToState = "PendingManager", EventId = "EVT-SUBMIT" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "Approved", EventId = "EVT-APPROVE" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "PendingDirector", EventId = "EVT-ESCALATE" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "Rejected", EventId = "EVT-REJECT" }
                ]
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Draft",
                Steps =
                [
                    new StepBlueprint { StepId = "Draft", StepType = "Command", NextSteps = new() { ["EVT-SUBMIT"] = "CheckAmount" } },
                    new StepBlueprint { StepId = "CheckAmount", StepType = "Command", NextSteps = new() { ["EVT-SUBMIT"] = "PendingManager" } },
                    new StepBlueprint
                    {
                        StepId = "PendingManager",
                        StepType = "HumanTask",
                        RequiredRoles = ["Manager"],
                        NextSteps = new() { ["EVT-APPROVE"] = "Approved", ["EVT-ESCALATE"] = "PendingDirector", ["EVT-REJECT"] = "Rejected" }
                    },
                    new StepBlueprint
                    {
                        StepId = "PendingDirector",
                        StepType = "HumanTask",
                        RequiredRoles = ["Director"],
                        NextSteps = new() { ["EVT-DIRECTOR-APPROVE"] = "Approved", ["EVT-DIRECTOR-REJECT"] = "Rejected" }
                    },
                    new StepBlueprint { StepId = "Approved", StepType = "Command", NextSteps = new() { ["Default"] = "END" } },
                    new StepBlueprint { StepId = "Rejected", StepType = "Command", NextSteps = new() { ["Default"] = "END" } }
                ]
            }
        };
        WorkflowSimulationGovernance.Apply(broken);
        var workflowClass = new WorkflowClass(tenantId, "ExpenseApprovalV2", "1.0.0", broken);
        var published = new WorkflowClassManager().Publish(workflowClass);
        Assert.True(published.IsValid, string.Join("; ", published.Errors.Select(error => $"{error.Code}: {error.Message}")));
        db.WorkflowClasses.Add(workflowClass);
        var compiled = WorkflowClassCompiler.MapToRuntimeDefinition(workflowClass);
        db.WorkflowDefinitions.Add(compiled);
        db.SaveChanges();

        await DataSeeder.RepairExpenseApprovalV2GraphAsync(db, tenantId);

        var repaired = await db.WorkflowClasses.SingleAsync(item => item.Name == "ExpenseApprovalV2");
        var checkAmount = repaired.Definition.Workflow.Steps.Single(step => step.StepId == "CheckAmount");
        Assert.Equal("Decision", checkAmount.StepType);
        Assert.Equal("PendingDirector", checkAmount.Conditions["Amount > 5000"]);
        Assert.Equal("CheckAmount", repaired.Definition.Workflow.Steps.Single(step => step.StepId == "Draft").NextSteps["EVT-SUBMIT"]);
        Assert.Contains(
            repaired.Definition.Workflow.Steps.Single(step => step.StepId == "PendingManager").RequiredRoles,
            role => role == "Manager");
        Assert.Contains(
            repaired.Definition.Workflow.Steps.Single(step => step.StepId == "PendingDirector").RequiredRoles,
            role => role == "Director");

        var definition = await db.WorkflowDefinitions.SingleAsync(item => item.Id == compiled.Id);
        Assert.Contains(definition.Steps, step => step.StepId == "CheckAmount" && step.StepType == WorkflowStepType.Decision);
        Assert.Equal("CheckAmount", definition.Steps.Single(step => step.StepId == "Draft").NextSteps["EVT-SUBMIT"]);

        var highValue = await Simulate(
            repaired.Definition,
            repaired.Name,
            new JObject { ["Amount"] = 75000, ["Currency"] = "USD" },
            "User",
            new JArray("EVT-SUBMIT"),
            false);
        Assert.True(highValue.Ok, highValue.Error);
        Assert.Equal("WaitingForHumanTask", highValue.Status);
        Assert.Equal("PendingDirector", highValue.CurrentStepId);
        Assert.Equal("PendingDirector", highValue.FinalState);

        var lowValue = await Simulate(
            repaired.Definition,
            repaired.Name,
            new JObject { ["Amount"] = 450, ["Currency"] = "USD" },
            "User",
            new JArray("EVT-SUBMIT"),
            false);
        Assert.True(lowValue.Ok, lowValue.Error);
        Assert.Equal("WaitingForHumanTask", lowValue.Status);
        Assert.Equal("PendingManager", lowValue.CurrentStepId);
        Assert.Equal("PendingManager", lowValue.FinalState);
    }

    private async Task<(bool Ok, string? Error, string? Status, string? CurrentStepId, string? FinalState)> Simulate(
        WorkflowClassBlueprint blueprint,
        string name,
        JObject payload,
        string role,
        JArray events,
        bool autoAdvanceTimers)
    {
        var call = await _tools.SimulateWorkflowClass(new JObject
        {
            ["blueprint"] = JObject.FromObject(blueprint),
            ["name"] = name,
            ["payload"] = payload,
            ["role"] = role,
            ["events"] = events,
            ["autoAdvanceTimers"] = autoAdvanceTimers
        });

        var json = JObject.Parse(call.Content[0].Text);
        if (call.IsError || json["ok"]?.Value<bool>() != true)
            return (false, json["error"]?["message"]?.ToString() ?? json.ToString(), null, null, null);

        var data = json["data"]!;
        return (true, null, data["status"]?.ToString(), data["currentStepId"]?.ToString(), data["finalState"]?.ToString());
    }

    private static void SeedExpenseSamples(FlowOSDbContext db, Guid tenantId)
    {
        var manager = new WorkflowClassManager();

        var expenseBp = new WorkflowClassBlueprint
        {
            Events =
            [
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request" },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request" },
                new EventBlueprint { EventId = "EVT-REJECT", Name = "Reject Request" }
            ],
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = ["Draft", "Pending", "Approved", "Rejected"],
                Transitions =
                [
                    new TransitionBlueprint { FromState = "Draft", ToState = "Pending", EventId = "EVT-SUBMIT" },
                    new TransitionBlueprint { FromState = "Pending", ToState = "Approved", EventId = "EVT-APPROVE" },
                    new TransitionBlueprint { FromState = "Pending", ToState = "Rejected", EventId = "EVT-REJECT" }
                ]
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Draft",
                Steps =
                [
                    new StepBlueprint { StepId = "Draft", StepType = "Command", NextSteps = new() { ["EVT-SUBMIT"] = "Pending" } },
                    new StepBlueprint { StepId = "Pending", StepType = "HumanTask", RequiredRoles = ["Approver"], NextSteps = new() { ["EVT-APPROVE"] = "Approved", ["EVT-REJECT"] = "Rejected" } },
                    new StepBlueprint { StepId = "Approved", StepType = "Command", NextSteps = new() { ["Default"] = "END" } },
                    new StepBlueprint { StepId = "Rejected", StepType = "Command", NextSteps = new() { ["Default"] = "END" } }
                ]
            },
            Roles =
            [
                new RoleBlueprint { Name = "Submitter", GrantedCapabilities = ["workflow.read", "event.publish.EVT-SUBMIT"] },
                new RoleBlueprint { Name = "Approver", GrantedCapabilities = ["workflow.read", "event.publish.EVT-APPROVE", "event.publish.EVT-REJECT"] }
            ],
            Capabilities =
            [
                new CapabilityBlueprint { Code = "workflow.read" },
                new CapabilityBlueprint { Code = "event.publish.EVT-SUBMIT" },
                new CapabilityBlueprint { Code = "event.publish.EVT-APPROVE" },
                new CapabilityBlueprint { Code = "event.publish.EVT-REJECT" }
            ]
        };
        WorkflowSimulationGovernance.Apply(expenseBp);
        var expense = new WorkflowClass(tenantId, "ExpenseApproval", "1.0.0", expenseBp);
        Assert.True(manager.Publish(expense).IsValid);
        db.WorkflowClasses.Add(expense);

        var expenseV2Bp = new WorkflowClassBlueprint
        {
            Events =
            [
                new EventBlueprint { EventId = "EVT-SUBMIT", Name = "Submit Request" },
                new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve Request" },
                new EventBlueprint { EventId = "EVT-REJECT", Name = "Reject Request" },
                new EventBlueprint { EventId = "EVT-DIRECTOR-APPROVE", Name = "Director Approve" },
                new EventBlueprint { EventId = "EVT-DIRECTOR-REJECT", Name = "Director Reject" },
                new EventBlueprint { EventId = "EVT-ESCALATE", Name = "Escalate to Director" }
            ],
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Draft",
                States = ["Draft", "PendingManager", "PendingDirector", "Approved", "Rejected"],
                Transitions =
                [
                    new TransitionBlueprint { FromState = "Draft", ToState = "PendingManager", EventId = "EVT-SUBMIT" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "Approved", EventId = "EVT-APPROVE" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "PendingDirector", EventId = "EVT-ESCALATE" },
                    new TransitionBlueprint { FromState = "PendingManager", ToState = "Rejected", EventId = "EVT-REJECT" },
                    new TransitionBlueprint { FromState = "PendingDirector", ToState = "Approved", EventId = "EVT-DIRECTOR-APPROVE" },
                    new TransitionBlueprint { FromState = "PendingDirector", ToState = "Rejected", EventId = "EVT-DIRECTOR-REJECT" }
                ]
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Draft",
                Steps =
                [
                    new StepBlueprint { StepId = "Draft", StepType = "Command", NextSteps = new() { ["EVT-SUBMIT"] = "PendingManager" } },
                    new StepBlueprint
                    {
                        StepId = "PendingManager",
                        StepType = "HumanTask",
                        RequiredRoles = ["Approver"],
                        NextSteps = new()
                        {
                            ["EVT-APPROVE"] = "Approved",
                            ["EVT-ESCALATE"] = "PendingDirector",
                            ["EVT-REJECT"] = "Rejected"
                        }
                    },
                    new StepBlueprint
                    {
                        StepId = "PendingDirector",
                        StepType = "HumanTask",
                        RequiredRoles = ["Director"],
                        NextSteps = new()
                        {
                            ["EVT-DIRECTOR-APPROVE"] = "Approved",
                            ["EVT-DIRECTOR-REJECT"] = "Rejected"
                        }
                    },
                    new StepBlueprint { StepId = "Approved", StepType = "Command", NextSteps = new() { ["Default"] = "END" } },
                    new StepBlueprint { StepId = "Rejected", StepType = "Command", NextSteps = new() { ["Default"] = "END" } }
                ]
            },
            Roles =
            [
                new RoleBlueprint { Name = "Submitter", GrantedCapabilities = ["workflow.read", "event.publish.EVT-SUBMIT"] },
                new RoleBlueprint { Name = "Approver", GrantedCapabilities = ["workflow.read", "event.publish.EVT-APPROVE", "event.publish.EVT-REJECT", "event.publish.EVT-ESCALATE"] },
                new RoleBlueprint { Name = "Director", GrantedCapabilities = ["workflow.read", "event.publish.EVT-DIRECTOR-APPROVE", "event.publish.EVT-DIRECTOR-REJECT"] }
            ],
            Capabilities =
            [
                new CapabilityBlueprint { Code = "workflow.read" },
                new CapabilityBlueprint { Code = "event.publish.EVT-SUBMIT" },
                new CapabilityBlueprint { Code = "event.publish.EVT-APPROVE" },
                new CapabilityBlueprint { Code = "event.publish.EVT-REJECT" },
                new CapabilityBlueprint { Code = "event.publish.EVT-ESCALATE" },
                new CapabilityBlueprint { Code = "event.publish.EVT-DIRECTOR-APPROVE" },
                new CapabilityBlueprint { Code = "event.publish.EVT-DIRECTOR-REJECT" }
            ]
        };
        WorkflowSimulationGovernance.Apply(expenseV2Bp);
        var expenseV2 = new WorkflowClass(tenantId, "ExpenseApprovalV2", "1.0.0", expenseV2Bp);
        Assert.True(manager.Publish(expenseV2).IsValid);
        db.WorkflowClasses.Add(expenseV2);
        db.SaveChanges();
    }
}
