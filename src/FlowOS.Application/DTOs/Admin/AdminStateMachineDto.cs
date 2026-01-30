using System.Collections.Generic;

namespace FlowOS.Application.DTOs.Admin;

public class AdminStateMachineDto
{
    public string EntityType { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<string> States { get; set; } = new();
    public List<AdminTransitionDto> Transitions { get; set; } = new();
}

public class AdminTransitionDto
{
    public string FromState { get; set; } = string.Empty;
    public string ToState { get; set; } = string.Empty;
    public string TriggerEvent { get; set; } = string.Empty; // Legacy support
    public string? EventId { get; set; } // New Event ID support
}
