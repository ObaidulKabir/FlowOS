using System;

namespace FlowOS.Security.Models;

public class Capability
{
    public string Code { get; private set; }
    public string Description { get; private set; }

    public Capability(string code, string description)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentNullException(nameof(code));
        
        Code = code;
        Description = description;
    }

    // Common Capabilities
    public static readonly Capability WorkflowCreate = new("workflow.create", "Create new workflow definitions");
    public static readonly Capability WorkflowStart = new("workflow.start", "Start new workflow instances");
    public static readonly Capability TaskApprove = new("task.approve", "Approve tasks");
    public static readonly Capability TaskReject = new("task.reject", "Reject tasks");
    public static readonly Capability SystemAdmin = new("system.admin", "Full system access");
}
