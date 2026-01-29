using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using Xunit;

namespace FlowOS.UnitTests.Domain;

public class TenantTests
{
    [Fact]
    public void Constructor_ShouldCreateActiveTenant()
    {
        // Arrange
        var name = "Test Tenant";

        // Act
        var tenant = new Tenant(name);

        // Assert
        Assert.NotNull(tenant);
        Assert.NotEqual(Guid.Empty, tenant.TenantId);
        Assert.Equal(name, tenant.Name);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal("{}", tenant.ConfigurationJson);
        Assert.True(tenant.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public void Suspend_ShouldChangeStatusToSuspended()
    {
        // Arrange
        var tenant = new Tenant("Test Tenant");

        // Act
        tenant.Suspend();

        // Assert
        Assert.Equal(TenantStatus.Suspended, tenant.Status);
        Assert.NotNull(tenant.UpdatedAt);
    }

    [Fact]
    public void UpdateConfiguration_ShouldUpdateJson()
    {
        // Arrange
        var tenant = new Tenant("Test Tenant");
        var newConfig = "{\"key\":\"value\"}";

        // Act
        tenant.UpdateConfiguration(newConfig);

        // Assert
        Assert.Equal(newConfig, tenant.ConfigurationJson);
        Assert.NotNull(tenant.UpdatedAt);
    }
}
