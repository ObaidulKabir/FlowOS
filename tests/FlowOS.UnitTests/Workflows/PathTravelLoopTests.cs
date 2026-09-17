using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Services;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class PathTravelLoopTests
{
    private readonly WorkflowEngine _engine = new(new StateMachineEngine());
    private readonly Guid _tenantId = Guid.NewGuid();

    private WorkflowDefinition CreateRetryPasswordWorkflow(int maxTravels = 3, string? onExceeded = "LockedOut")
    {
        var def = new WorkflowDefinition(_tenantId, "RetryPassword", 1, "EnterPassword");

        var enter = new WorkflowStepDefinition("EnterPassword", WorkflowStepType.HumanTask);
        enter.NextSteps.Add("PASSWORD_OK", "Unlocked");
        enter.NextSteps.Add("PASSWORD_FAIL", "EnterPassword");
        enter.PathLimits["PASSWORD_FAIL"] = new PathTravelLimit(maxTravels, onExceeded);
        def.AddStep(enter);

        var unlocked = new WorkflowStepDefinition("Unlocked", WorkflowStepType.Command);
        unlocked.NextSteps.Add("Default", "END");
        def.AddStep(unlocked);

        var locked = new WorkflowStepDefinition("LockedOut", WorkflowStepType.Command);
        locked.NextSteps.Add("Default", "END");
        def.AddStep(locked);

        def.Publish();
        return def;
    }

    [Fact]
    public void PathTravelRules_Detects_SelfLoop_And_BackEdge()
    {
        var steps = new List<StepBlueprint>
        {
            new()
            {
                StepId = "EnterPassword",
                StepType = "HumanTask",
                NextSteps = new Dictionary<string, string>
                {
                    ["PASSWORD_OK"] = "Unlocked",
                    ["PASSWORD_FAIL"] = "EnterPassword"
                }
            },
            new()
            {
                StepId = "Unlocked",
                StepType = "Command",
                NextSteps = new Dictionary<string, string> { ["Default"] = "END" }
            }
        };

        var edges = PathTravelRules.CollectEdges(steps);
        var cyclic = PathTravelRules.FindCyclicEdgeKeys(edges);

        Assert.Contains(PathTravelRules.EdgeKey("EnterPassword", "PASSWORD_FAIL", "EnterPassword"), cyclic);
        Assert.DoesNotContain(PathTravelRules.EdgeKey("EnterPassword", "PASSWORD_OK", "Unlocked"), cyclic);
    }

    [Fact]
    public void Advance_RetryPassword_Allows_Declared_Travels_Then_Overflows()
    {
        var def = CreateRetryPasswordWorkflow(maxTravels: 3, onExceeded: "LockedOut");
        var instance = new WorkflowInstance(_tenantId, def.Id, Guid.Empty, def.Version, "EnterPassword");
        var context = new FlowOS.StateMachines.Models.ExecutionContext();

        for (var i = 1; i <= 3; i++)
        {
            var allowed = _engine.Advance(instance, def, new TestDomainEvent(_tenantId, "PASSWORD_FAIL"), context);
            Assert.True(allowed.Success, allowed.Message);
            Assert.Equal("EnterPassword", instance.CurrentStepId);
            Assert.Equal(i, instance.GetPathTravelCount(PathTravelRules.EdgeKey("EnterPassword", "PASSWORD_FAIL", "EnterPassword")));
        }

        var overflow = _engine.Advance(instance, def, new TestDomainEvent(_tenantId, "PASSWORD_FAIL"), context);
        Assert.True(overflow.Success, overflow.Message);
        Assert.Equal("LockedOut", instance.CurrentStepId);
        Assert.Equal(4, instance.GetPathTravelCount(PathTravelRules.EdgeKey("EnterPassword", "PASSWORD_FAIL", "EnterPassword")));
    }

    [Fact]
    public void Advance_RetryPassword_Without_OnExceeded_FailsClosed()
    {
        var def = CreateRetryPasswordWorkflow(maxTravels: 1, onExceeded: null);
        var instance = new WorkflowInstance(_tenantId, def.Id, Guid.Empty, def.Version, "EnterPassword");
        var context = new FlowOS.StateMachines.Models.ExecutionContext();

        var first = _engine.Advance(instance, def, new TestDomainEvent(_tenantId, "PASSWORD_FAIL"), context);
        Assert.True(first.Success, first.Message);

        var blocked = _engine.Advance(instance, def, new TestDomainEvent(_tenantId, "PASSWORD_FAIL"), context);
        Assert.False(blocked.Success);
        Assert.Contains("Path travel limit exceeded", blocked.Message);
        Assert.Equal("EnterPassword", instance.CurrentStepId);
    }

    [Fact]
    public void Advance_Acyclic_Success_Path_Is_Unlimited()
    {
        var def = CreateRetryPasswordWorkflow();
        var instance = new WorkflowInstance(_tenantId, def.Id, Guid.Empty, def.Version, "EnterPassword");

        var result = _engine.Advance(instance, def, new TestDomainEvent(_tenantId, "PASSWORD_OK"), new FlowOS.StateMachines.Models.ExecutionContext());

        Assert.True(result.Success, result.Message);
        Assert.Equal("Unlocked", instance.CurrentStepId);
    }

    [Fact]
    public void Advance_Undeclared_Cycle_Uses_Default_Cap_Of_Five()
    {
        var def = new WorkflowDefinition(_tenantId, "UndeclaredRetry", 1, "EnterPassword");
        var enter = new WorkflowStepDefinition("EnterPassword", WorkflowStepType.HumanTask);
        enter.NextSteps.Add("PASSWORD_FAIL", "EnterPassword");
        def.AddStep(enter);
        def.Publish();

        var instance = new WorkflowInstance(_tenantId, def.Id, Guid.Empty, def.Version, "EnterPassword");
        var context = new FlowOS.StateMachines.Models.ExecutionContext();

        for (var i = 1; i <= PathTravelRules.DefaultMaxTravels; i++)
        {
            var allowed = _engine.Advance(instance, def, new TestDomainEvent(_tenantId, "PASSWORD_FAIL"), context);
            Assert.True(allowed.Success, allowed.Message);
        }

        var blocked = _engine.Advance(instance, def, new TestDomainEvent(_tenantId, "PASSWORD_FAIL"), context);
        Assert.False(blocked.Success);
        Assert.Contains("Path travel limit exceeded", blocked.Message);
    }

    [Fact]
    public void Validator_Rejects_Invalid_PathLimits()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "PASSWORD_FAIL" },
                new() { EventId = "PASSWORD_OK" }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Authenticating",
                States = new List<string> { "Authenticating", "Unlocked" }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "EnterPassword",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "EnterPassword",
                        StepType = "HumanTask",
                        NextSteps = new Dictionary<string, string>
                        {
                            ["PASSWORD_OK"] = "END",
                            ["PASSWORD_FAIL"] = "EnterPassword"
                        },
                        PathLimits = new Dictionary<string, PathTravelLimitBlueprint>
                        {
                            ["PASSWORD_FAIL"] = new() { MaxTravels = 0, OnExceeded = "MissingStep" },
                            ["UNKNOWN_EVENT"] = new() { MaxTravels = 3 }
                        }
                    }
                }
            }
        };

        var result = validator.Validate(blueprint);

        Assert.Contains(result.Errors, e => e.Code == "WF-LOOP-001");
        Assert.Contains(result.Errors, e => e.Code == "WF-LOOP-002");
        Assert.Contains(result.Errors, e => e.Code == "WF-LOOP-003");
    }

    private sealed class TestDomainEvent : FlowOS.Events.Models.DomainEvent
    {
        public TestDomainEvent(Guid tenantId, string eventType) : base(tenantId, eventType)
        {
        }
    }
}
