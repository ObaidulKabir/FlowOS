using System.ComponentModel.DataAnnotations;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Validation;

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

public record GenerateBlueprintCopilotRequest
{
    [Required]
    public string Prompt { get; init; } = string.Empty;

    public WorkflowClassBlueprint? CurrentBlueprint { get; init; }

    public string Mode { get; init; } = "create"; // "create" | "refine" | "template"
}

public record GenerateBlueprintCopilotResponse
{
    public string SuggestedName { get; init; } = string.Empty;
    public string SuggestedVersion { get; init; } = "1.0.0";
    public string Summary { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public WorkflowClassBlueprint Blueprint { get; init; } = new();
    public FlowOS.Domain.Validation.ValidationResult Validation { get; init; } = new();
}

