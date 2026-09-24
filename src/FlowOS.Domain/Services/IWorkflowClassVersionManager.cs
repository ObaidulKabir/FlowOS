using System;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Services;

public interface IWorkflowClassVersionManager
{
    WorkflowClass CreateCopyForTenant(WorkflowClass sourceClass, Guid newTenantId);
    WorkflowClass CreateNewVersion(WorkflowClass sourceClass, string newVersion);
    WorkflowClass CreateNewVersion(WorkflowClass sourceClass, VersionBumpType bumpType, string? changeLog = null);
}
