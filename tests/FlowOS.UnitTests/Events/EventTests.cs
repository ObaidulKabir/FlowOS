using FlowOS.Events.Models;
using Xunit;

namespace FlowOS.UnitTests.Events;

public class TestDomainEvent : DomainEvent
{
    public TestDomainEvent(Guid tenantId) : base(tenantId, "TestEvent")
    {
    }
}

public class EventTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        // Act
        var evt = new TestDomainEvent(tenantId);

        // Assert
        Assert.NotEqual(Guid.Empty, evt.EventId);
        Assert.Equal(tenantId, evt.TenantId);
        Assert.Equal("TestEvent", evt.EventType);
        Assert.Equal(1, evt.Version);
        Assert.True(evt.Timestamp > DateTime.MinValue);
        Assert.NotNull(evt.Metadata);
    }

    [Fact]
    public void AddMetadata_ShouldStoreValue()
    {
        // Arrange
        var evt = new TestDomainEvent(Guid.NewGuid());

        // Act
        evt.AddMetadata("Risk", "High");

        // Assert
        Assert.Contains("Risk", evt.Metadata.Keys);
        Assert.Equal("High", evt.Metadata["Risk"]);
    }
}
