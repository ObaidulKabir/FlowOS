using System;
using System.Collections.Generic;
using FlowOS.Domain.Enums;
using FlowOS.Domain.ValueObjects;

namespace FlowOS.Domain.Entities;

public class StateMachineDefinition
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string EntityType { get; private set; }
    public int Version { get; private set; }
    public string InitialState { get; private set; }
    public StateMachineStatus Status { get; private set; }
    
    public HashSet<string> States { get; private set; }
    public List<StateTransition> Transitions { get; private set; }

    protected StateMachineDefinition() 
    {
        EntityType = null!;
        InitialState = null!;
        States = new HashSet<string>();
        Transitions = new List<StateTransition>();
    }

    public StateMachineDefinition(Guid tenantId, string entityType, string initialState, int version = 1)
    {
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentNullException(nameof(entityType));
        if (string.IsNullOrWhiteSpace(initialState)) throw new ArgumentNullException(nameof(initialState));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        EntityType = entityType;
        InitialState = initialState;
        Version = version;
        Status = StateMachineStatus.Draft;
        States = new HashSet<string> { initialState };
        Transitions = new List<StateTransition>();
    }

    public void AddState(string state)
    {
        if (Status != StateMachineStatus.Draft)
            throw new InvalidOperationException("Cannot modify definition after publication.");
        
        States.Add(state);
    }

    public void AddTransition(StateTransition transition)
    {
        if (Status != StateMachineStatus.Draft)
            throw new InvalidOperationException("Cannot modify definition after publication.");

        if (!States.Contains(transition.FromState))
            throw new InvalidOperationException($"State '{transition.FromState}' is not defined.");
        
        if (!States.Contains(transition.ToState))
            throw new InvalidOperationException($"State '{transition.ToState}' is not defined.");

        Transitions.Add(transition);
    }

    public void Publish()
    {
        Status = StateMachineStatus.Published;
    }

    public void Archive()
    {
        Status = StateMachineStatus.Archived;
    }
}
