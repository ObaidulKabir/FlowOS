using System;

namespace FlowOS.Application.Common.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class RequiresCapabilityAttribute : Attribute
{
    public string Capability { get; }

    public RequiresCapabilityAttribute(string capability)
    {
        Capability = capability;
    }
}
