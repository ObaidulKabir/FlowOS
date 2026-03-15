using System;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Validation;

namespace FlowOS.Domain.Services;

public class WorkflowClassManager
{
    private readonly WorkflowClassValidator _validator;

    public WorkflowClassManager(WorkflowClassValidator validator)
    {
        _validator = validator;
    }

    /// <summary>
    /// Creates a new Draft WorkflowClass with initial validation.
    /// </summary>
    /// <param name="workflowClass">The draft workflow class to create.</param>
    /// <returns>Validation result. If invalid, the draft should not be persisted.</returns>
    public ValidationResult CreateDraft(WorkflowClass workflowClass)
    {
        // 1. Basic Validation (Structure, Completeness)
        // Even a draft should have a valid structure to be useful.
        // We allow some looseness (e.g. maybe not all roles assigned), 
        // but the graph integrity (transitions to known steps) should be sound to prevent runtime crashes if tested.
        
        var result = _validator.Validate(workflowClass);
        
        // If validation fails, we return the errors.
        // The caller (Application Layer) should decide whether to block creation or allow "Invalid Draft".
        // FlowOS Philosophy: "Law before Work" -> Invalid definitions should typically be rejected or flagged as "Needs Repair".
        // For strictness, we return the result.
        
        return result;
    }

    public ValidationResult Publish(WorkflowClass workflowClass)
    {
        // 1. Validate
        var result = _validator.Validate(workflowClass);
        if (!result.IsValid) return result;

        // 2. Transition
        try 
        {
            workflowClass.Publish();
        }
        catch (InvalidOperationException ex)
        {
            result.AddError("LIF-001", "Lifecycle", ex.Message, "Status");
        }

        return result;
    }

    public ValidationResult SubmitForReview(WorkflowClass workflowClass)
    {
        var result = new ValidationResult();
        // Additional Shared-scope validation could go here
        
        try
        {
            workflowClass.SubmitForReview();
        }
        catch (InvalidOperationException ex)
        {
            result.AddError("LIF-002", "Lifecycle", ex.Message, "Status");
        }
        return result;
    }

    public ValidationResult WithdrawSubmission(WorkflowClass workflowClass)
    {
        var result = new ValidationResult();
        try
        {
            workflowClass.WithdrawSubmission();
        }
        catch (InvalidOperationException ex)
        {
            result.AddError("LIF-004", "Lifecycle", ex.Message, "Status");
        }
        return result;
    }

    public ValidationResult Deprecate(WorkflowClass workflowClass)
    {
        var result = new ValidationResult();
        // Rule: Only Published (Private) or Public can be deprecated?
        // Entity allows any non-deprecated. 
        // Prompt Matrix: Published (Private) -> Deprecate. 
        // Draft -> Delete.
        
        if (workflowClass.Status == WorkflowClassStatus.Draft)
        {
            result.AddError("LIF-005", "Lifecycle", "Drafts should be deleted, not deprecated.", "Status");
            return result;
        }

        workflowClass.Deprecate();
        return result;
    }
    
    public ValidationResult ValidateOnly(WorkflowClass workflowClass)
    {
        return _validator.Validate(workflowClass);
    }

    public ValidationResult ApproveAsPublic(WorkflowClass workflowClass)
    {
        var result = new ValidationResult();
        // Public scope validation
        if (workflowClass.Definition.Roles.Count > 0) 
        {
             // Warning or Error? Prompt says "Public: reusable contract only".
             // "Roles are symbolic only".
        }

        try
        {
            workflowClass.ApproveAsPublic();
        }
        catch (InvalidOperationException ex)
        {
            result.AddError("LIF-003", "Lifecycle", ex.Message, "Status");
        }
        return result;
    }
}
