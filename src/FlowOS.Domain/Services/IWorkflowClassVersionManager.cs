using System;
using FlowOS.Domain.Entities;

namespace FlowOS.Domain.Services;

public interface IWorkflowClassVersionManager
{
    WorkflowClass CreateCopyForTenant(WorkflowClass sourceClass, Guid newTenantId);
    WorkflowClass CreateNewVersion(WorkflowClass sourceClass, string newVersion);
}
