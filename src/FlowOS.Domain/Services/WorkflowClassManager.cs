using System;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Validation;

namespace FlowOS.Domain.Services;

public class WorkflowClassManager : IWorkflowClassManager
{
    private readonly WorkflowClassValidator _validator;

    public WorkflowClassManager(WorkflowClassValidator validator = null)
    {
        _validator = validator ?? new WorkflowClassValidator();
    }

    public ValidationResult ValidateOnly(WorkflowClass workflowClass)
    {
        return _validator.Validate(workflowClass);
    }

    public ValidationResult CreateDraft(WorkflowClass workflowClass)
    {
        var result = _validator.Validate(workflowClass);
        if (!result.IsValid)
            return result;

        if (workflowClass.Status != Enums.WorkflowClassStatus.Draft)
        {
            result.AddError("STATE", "Lifecycle", "Cannot create draft from a non-draft state.", "Lifecycle");
        }
        return result;
    }

    public ValidationResult Publish(WorkflowClass workflowClass)
    {
        var result = _validator.Validate(workflowClass);
        if (!result.IsValid)
            return result;

        if (workflowClass.Status != Enums.WorkflowClassStatus.Draft)
        {
            result.AddError("STATE", "Lifecycle", $"Cannot publish from state {workflowClass.Status}.", "Lifecycle");
            return result;
        }

        workflowClass.Status = Enums.WorkflowClassStatus.Published;
        workflowClass.PublishedAt = DateTime.UtcNow;

        return result;
    }

    public ValidationResult SubmitForReview(WorkflowClass workflowClass)
    {
        var result = new ValidationResult();
        if (workflowClass.Status != Enums.WorkflowClassStatus.Published)
        {
            result.AddError("STATE", "Lifecycle", "Must be Published (Private) before submitting for review.", "Lifecycle");
            return result;
        }

        workflowClass.Scope = Enums.WorkflowClassScope.Shared;
        workflowClass.Status = Enums.WorkflowClassStatus.Shared;

        return result;
    }

    public ValidationResult WithdrawSubmission(WorkflowClass workflowClass)
    {
        var result = new ValidationResult();
        if (workflowClass.Status != Enums.WorkflowClassStatus.Shared)
        {
            result.AddError("STATE", "Lifecycle", "Only Shared (Under Review) classes can be withdrawn.", "Lifecycle");
            return result;
        }

        workflowClass.Scope = Enums.WorkflowClassScope.Private;
        workflowClass.Status = Enums.WorkflowClassStatus.Published;

        return result;
    }

    public ValidationResult ApproveAsPublic(WorkflowClass workflowClass)
    {
        var result = new ValidationResult();
        if (workflowClass.Status != Enums.WorkflowClassStatus.Shared)
        {
            result.AddError("STATE", "Lifecycle", "Must be Shared before becoming Public.", "Lifecycle");
            return result;
        }

        workflowClass.Scope = Enums.WorkflowClassScope.Public;
        workflowClass.Status = Enums.WorkflowClassStatus.Public;

        return result;
    }

    public ValidationResult Deprecate(WorkflowClass workflowClass, string? reason = null, Guid? migrationTargetId = null)
    {
        var result = new ValidationResult();
        if (workflowClass.Status == Enums.WorkflowClassStatus.Deprecated)
        {
            return result; // Already deprecated
        }

        workflowClass.SetDeprecation(reason, migrationTargetId);
        workflowClass.Status = Enums.WorkflowClassStatus.Deprecated;

        return result;
    }

    public ValidationResult Rollback(WorkflowClass currentVersion, WorkflowClass previousVersion)
    {
        var result = new ValidationResult();

        if (previousVersion.Status != Enums.WorkflowClassStatus.Published &&
            previousVersion.Status != Enums.WorkflowClassStatus.Deprecated)
        {
            result.AddError("ROLLBACK", "Lifecycle", 
                $"Cannot rollback to version {previousVersion.Version} — it is in status {previousVersion.Status}. Only Published or Deprecated versions can be rollback targets.", 
                "Lifecycle");
            return result;
        }

        if (currentVersion.Status == Enums.WorkflowClassStatus.Draft)
        {
            result.AddError("ROLLBACK", "Lifecycle",
                "Cannot rollback a Draft version. Delete it instead.",
                "Lifecycle");
            return result;
        }

        // Deprecate the current version
        currentVersion.SetDeprecation($"Rolled back in favor of version {previousVersion.Version}.", previousVersion.Id);
        currentVersion.Status = Enums.WorkflowClassStatus.Deprecated;

        return result;
    }
}