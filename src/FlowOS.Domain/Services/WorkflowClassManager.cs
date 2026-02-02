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
