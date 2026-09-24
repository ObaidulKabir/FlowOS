using System;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.ValueObjects;

namespace FlowOS.Domain.Services;

public class WorkflowClassVersionManager : IWorkflowClassVersionManager
{
    public WorkflowClass CreateCopyForTenant(WorkflowClass sourceClass, Guid newTenantId)
    {
        var copy = new WorkflowClass(newTenantId, sourceClass.Name, "1.0.0", sourceClass.Definition);
        return copy;
    }

    public WorkflowClass CreateNewVersion(WorkflowClass sourceClass, string newVersion)
    {
        if (string.IsNullOrWhiteSpace(newVersion)) throw new ArgumentNullException(nameof(newVersion));
        
        var newClass = new WorkflowClass(sourceClass.TenantId, sourceClass.Name, newVersion, sourceClass.Definition);
        newClass.PreviousVersionId = sourceClass.Id;
        return newClass;
    }

    public WorkflowClass CreateNewVersion(WorkflowClass sourceClass, VersionBumpType bumpType, string? changeLog = null)
    {
        var current = WorkflowVersion.Parse(sourceClass.Version);
        var bumped = current.Bump(bumpType);
        var newClass = CreateNewVersion(sourceClass, bumped.ToString());
        if (!string.IsNullOrWhiteSpace(changeLog))
        {
            newClass.UpdateDraft(newClass.Name, newClass.Version, newClass.Definition, changeLog);
        }
        return newClass;
    }
}
