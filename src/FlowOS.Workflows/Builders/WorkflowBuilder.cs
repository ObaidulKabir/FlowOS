using System;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Builders;

public class WorkflowBuilder
{
    private readonly Guid _tenantId;
    private readonly string _name;
    private readonly int _version;
    private readonly WorkflowDefinition _definition;

    private WorkflowBuilder(Guid tenantId, string name, int version)
    {
        _tenantId = tenantId;
        _name = name;
        _version = version;
        _definition = new WorkflowDefinition(tenantId, name, version);
    }

    public static WorkflowBuilder Create(Guid tenantId, string name, int version = 1)
    {
        return new WorkflowBuilder(tenantId, name, version);
    }

    public WorkflowStepBuilder AddStep(string stepId, WorkflowStepType type)
    {
        var step = new WorkflowStepDefinition(stepId, type);
        _definition.AddStep(step);
        return new WorkflowStepBuilder(this, step);
    }

    public WorkflowStepBuilder StartWith(string stepId, WorkflowStepType type = WorkflowStepType.Command)
    {
        return AddStep(stepId, type);
    }

    public WorkflowDefinition Build()
    {
        return _definition;
    }
}

public class WorkflowStepBuilder
{
    private readonly WorkflowBuilder _parent;
    private readonly WorkflowStepDefinition _step;

    public WorkflowStepBuilder(WorkflowBuilder parent, WorkflowStepDefinition step)
    {
        _parent = parent;
        _step = step;
    }

    public WorkflowStepBuilder On(string eventId, string nextStepId)
    {
        _step.NextSteps[eventId] = nextStepId;
        return this;
    }

    public WorkflowBuilder Then(string nextStepId)
    {
        _step.NextSteps["Default"] = nextStepId;
        return _parent;
    }

    public WorkflowBuilder Next(string eventId, string nextStepId)
    {
        _step.NextSteps[eventId] = nextStepId;
        return _parent; // Return parent to allow adding next step immediately
    }
    
    // For when you want to add multiple transitions and then go back
    public WorkflowBuilder CompleteStep()
    {
        return _parent;
    }

    public WorkflowDefinition Build()
    {
        return _parent.Build();
    }
}
