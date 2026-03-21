using System;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Services;

public class WorkflowClassVersionManager : IWorkflowClassVersionManager
{
    public WorkflowClass CreateCopyForTenant(WorkflowClass sourceClass, Guid newTenantId)
    {
        var copy = new WorkflowClass(newTenantId, sourceClass.Name, "1.0.0", sourceClass.Definition);
        // We set internal properties after instantiation
        // We need to make sure we can set these or we use the constructor
        return copy;
    }

    public WorkflowClass CreateNewVersion(WorkflowClass sourceClass, string newVersion)
    {
        if (string.IsNullOrWhiteSpace(newVersion)) throw new ArgumentNullException(nameof(newVersion));
        
        // Create a new Draft copy with the specified version
        var newClass = new WorkflowClass(sourceClass.TenantId, sourceClass.Name, newVersion, sourceClass.Definition);
        newClass.PreviousVersionId = sourceClass.Id;
        return newClass;
    }
}
