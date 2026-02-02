using System;
using System.Collections.Generic;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;

namespace FlowOS.Domain.Builders;

public class StateMachineBuilder
{
    private readonly Guid _tenantId;
    private readonly string _entityType;
    private readonly int _version;
    private string _initialState = string.Empty;
    private readonly HashSet<string> _states = new();
    private readonly List<StateTransition> _transitions = new();

    private StateMachineBuilder(Guid tenantId, string entityType, int version)
    {
        _tenantId = tenantId;
        _entityType = entityType;
        _version = version;
    }

    public static StateMachineBuilder Create(Guid tenantId, string entityType, int version = 1)
    {
        return new StateMachineBuilder(tenantId, entityType, version);
    }

    public StateMachineBuilder WithInitialState(string state)
    {
        _initialState = state;
        _states.Add(state);
        return this;
    }

    public StateMachineBuilder AddState(string state)
    {
        _states.Add(state);
        return this;
    }

    public StateMachineBuilder AddTransition(string from, string to, string eventId)
    {
        _states.Add(from);
        _states.Add(to);
        _transitions.Add(new StateTransition(from, to, eventId) { EventId = eventId });
        return this;
    }

    public StateMachineDefinition Build()
    {
        if (string.IsNullOrEmpty(_initialState))
            throw new InvalidOperationException("Initial state must be set.");

        var sm = new StateMachineDefinition(_tenantId, _entityType, _initialState, _version);
        
        foreach (var state in _states)
        {
            sm.AddState(state);
        }

        foreach (var transition in _transitions)
        {
            sm.AddTransition(transition);
        }

        return sm;
    }
}
