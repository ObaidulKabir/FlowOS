using FlowOS.Workflows.Domain;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

/// <summary>CR-10: the caller's own reference stored on a workflow instance.</summary>
public class WorkflowInstanceExternalReferenceTests
{
    private static WorkflowInstance NewInstance() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 1, "Start");

    [Fact]
    public void Values_are_trimmed()
    {
        var instance = NewInstance();
        instance.SetExternalReference("  learning-os ", " case-1 ");
        Assert.Equal("learning-os", instance.ExternalSourceSystem);
        Assert.Equal("case-1", instance.ExternalReference);
    }

    [Fact]
    public void Empty_values_clear_the_reference()
    {
        var instance = NewInstance();
        instance.SetExternalReference("learning-os", "case-1");
        instance.SetExternalReference(" ", null);
        Assert.Null(instance.ExternalSourceSystem);
        Assert.Null(instance.ExternalReference);
    }

    [Fact]
    public void Limits_are_enforced()
    {
        var instance = NewInstance();
        instance.SetExternalReference(new string('s', 100), new string('r', 200));
        Assert.Throws<ArgumentException>(() => instance.SetExternalReference(new string('s', 101), "ok"));
        Assert.Throws<ArgumentException>(() => instance.SetExternalReference("ok", new string('r', 201)));
    }
}
