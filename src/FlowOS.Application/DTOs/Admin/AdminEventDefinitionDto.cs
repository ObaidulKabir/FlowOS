using System;

namespace FlowOS.Application.DTOs.Admin;

public class AdminEventDefinitionDto
{
    public string EventId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
