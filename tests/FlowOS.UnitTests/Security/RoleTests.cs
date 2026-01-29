using System;
using FlowOS.Security.Models;
using Xunit;

namespace FlowOS.UnitTests.Security;

public class RoleTests
{
    [Fact]
    public void CreateRole_ShouldInitializeCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var role = new Role(tenantId, "Admin");

        Assert.Equal(tenantId, role.TenantId);
        Assert.Equal("Admin", role.Name);
        Assert.NotNull(role.Permissions);
        Assert.Empty(role.Permissions);
    }

    [Fact]
    public void AddPermission_ShouldAddUniquePermissions()
    {
        var role = new Role(Guid.NewGuid(), "Admin");
        role.AddPermission("STATE.TRANSITION.REQUEST");
        role.AddPermission("STATE.TRANSITION.REQUEST"); // Duplicate

        Assert.Single(role.Permissions);
        Assert.Contains("STATE.TRANSITION.REQUEST", role.Permissions);
    }
}
