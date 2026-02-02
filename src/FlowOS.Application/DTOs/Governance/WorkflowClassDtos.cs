using System.ComponentModel.DataAnnotations;
using FlowOS.Domain.Blueprints;

namespace FlowOS.Application.DTOs.Governance;

public record CreateWorkflowClassRequest
{
    [Required]
    public string Name { get; init; } = string.Empty;
    
    [Required]
    public string Version { get; init; } = "1.0.0";
    
    [Required]
    public WorkflowClassBlueprint Definition { get; init; } = new();
}

public record CopyWorkflowClassRequest
{
    [Required]
    public Guid NewTenantId { get; init; }
}
