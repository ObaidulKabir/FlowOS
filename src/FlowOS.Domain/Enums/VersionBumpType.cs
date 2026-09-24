namespace FlowOS.Domain.Enums;

/// <summary>
/// Specifies the type of version bump when creating a new WorkflowClass version.
/// </summary>
public enum VersionBumpType
{
    /// <summary>Non-breaking change, same structure (e.g. role description fix). 1.0.0 → 1.0.1</summary>
    Patch = 0,
    /// <summary>Backwards-compatible change (e.g. new optional step). 1.0.0 → 1.1.0</summary>
    Minor = 1,
    /// <summary>Breaking change (e.g. removed step, changed transitions). 1.0.0 → 2.0.0</summary>
    Major = 2
}
