using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Agents.Abstractions;
using FlowOS.Application.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Agents;

/// <summary>
/// CR-11: AI may suggest on consequential steps but never commits there, and a tenant can turn agent
/// auto-commit off entirely. Both blocks win over any autoCommit policy or confidence.
/// </summary>
public class AutoCommitGuardrailTests
{
    private static StepBlueprint AgentStep(bool consequential) => new()
    {
        StepId = "Assess",
        StepType = "HumanTask",
        Actor = "Agent",
        DecisionGuideline = "Decide",
        Consequential = consequential,
        AutoCommit = new StepAutoCommitBlueprint { MinConfidence = 0.5, AllowedEvents = new List<string> { "EVT-CERTIFY" } },
        NextSteps = new Dictionary<string, string> { ["EVT-CERTIFY"] = "END" }
    };

    private static readonly SuggestedAction Certify = new("EVT-CERTIFY", "Mastery shown", 0.99, new Dictionary<string, object>());

    [Fact]
    public void A_permissive_policy_commits_on_an_ordinary_step()
    {
        var packet = DecisionPacketFactory.CreateForSimulation(AgentStep(consequential: false));

        Assert.True(AutoCommitEvaluator.CanCommit(packet, Certify, out var reason), reason);
        Assert.Null(packet.AutoCommitBlockedReason);
    }

    [Fact]
    public void A_consequential_step_is_never_auto_committed()
    {
        var packet = DecisionPacketFactory.CreateForSimulation(AgentStep(consequential: true));

        Assert.False(AutoCommitEvaluator.CanCommit(packet, Certify, out var reason));
        Assert.Equal(DecisionPacket.ConsequentialStepReason, reason);
    }

    [Fact]
    public void A_tenant_block_wins_over_a_permissive_policy()
    {
        var packet = DecisionPacketFactory.CreateForSimulation(AgentStep(consequential: false))
            with { AutoCommitBlockedReason = DecisionPacket.TenantDisabledReason };

        Assert.NotNull(packet.AutoCommit);
        Assert.False(AutoCommitEvaluator.CanCommit(packet, Certify, out var reason));
        Assert.Equal(DecisionPacket.TenantDisabledReason, reason);
    }

    [Fact]
    public void Validator_rejects_autoCommit_on_a_consequential_step()
    {
        var blueprint = Blueprint(AgentStep(consequential: true));

        var errors = new WorkflowClassValidator().Validate(blueprint).Errors;

        Assert.Contains(errors, e => e.Code == "WF-AGENT-006" && e.Message.Contains("'Assess'"));
    }

    [Fact]
    public void Validator_accepts_a_consequential_step_without_autoCommit()
    {
        var step = AgentStep(consequential: true);
        step.AutoCommit = null;

        var errors = new WorkflowClassValidator().Validate(Blueprint(step)).Errors;

        Assert.DoesNotContain(errors, e => e.Code == "WF-AGENT-006");
    }

    [Fact]
    public void Compiler_carries_the_flag_to_the_runtime_step()
    {
        var step = AgentStep(consequential: true);
        step.AutoCommit = null;
        var workflowClass = new WorkflowClass(Guid.NewGuid(), "Certification", "1.0.0", Blueprint(step));

        var definition = WorkflowClassCompiler.MapToRuntimeDefinition(workflowClass);

        Assert.True(definition.Steps.Single(s => s.StepId == "Assess").Consequential);
    }

    [Fact]
    public async Task Tenant_switch_defaults_on_and_can_be_turned_off()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>().UseInMemoryDatabase("autocommit_" + Guid.NewGuid()).Options;
        await using var db = new FlowOSDbContext(options);
        var tenant = new Tenant("Guardrail school");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var policy = new TenantAgentAutoCommitPolicy(db);

        Assert.True(await policy.IsAutoCommitEnabledAsync(tenant.TenantId));
        Assert.True(await policy.IsAutoCommitEnabledAsync(Guid.NewGuid())); // no row: previous behaviour

        tenant.SetAgentAutoCommit(false, new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        Assert.False(await policy.IsAutoCommitEnabledAsync(tenant.TenantId));
    }

    private static WorkflowClassBlueprint Blueprint(StepBlueprint step) => new()
    {
        Events = new List<EventBlueprint> { new() { EventId = "EVT-CERTIFY", Name = "Certify" } },
        StateMachine = new StateMachineBlueprint
        {
            InitialState = "Assessing",
            States = new List<string> { "Assessing", "Certified" },
            Transitions = new List<TransitionBlueprint> { new() { FromState = "Assessing", ToState = "Certified", EventId = "EVT-CERTIFY" } }
        },
        Workflow = new WorkflowBlueprint { StartStepId = step.StepId, Steps = new List<StepBlueprint> { step } }
    };
}
