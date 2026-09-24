using FlowOS.Domain.Entities;
using FlowOS.Domain.Validation;
using System;

namespace FlowOS.Domain.Services;

public interface IWorkflowClassManager 
{ 
    ValidationResult ApproveAsPublic(WorkflowClass workflowClass); 
    ValidationResult CreateDraft(WorkflowClass workflowClass); 
    ValidationResult Deprecate(WorkflowClass workflowClass, string? reason = null, Guid? migrationTargetId = null); 
    ValidationResult Publish(WorkflowClass workflowClass); 
    ValidationResult SubmitForReview(WorkflowClass workflowClass); 
    ValidationResult ValidateOnly(WorkflowClass workflowClass); 
    ValidationResult WithdrawSubmission(WorkflowClass workflowClass);
    ValidationResult Rollback(WorkflowClass currentVersion, WorkflowClass previousVersion);
}