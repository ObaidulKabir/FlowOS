using FlowOS.Api.Services;
using FlowOS.Core.Interfaces;
using Moq;

namespace FlowOS.UnitTests.Api;

public class AdminTenantScopeTests
{
    private static readonly Guid Platform = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Demo = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Other = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static ICurrentUser User(Guid tenant, params string[] roles)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.TenantId).Returns(tenant);
        user.SetupGet(u => u.Roles).Returns(roles.ToList());
        return user.Object;
    }

    [Fact]
    public void PlatformAdmin_CanReadAnotherTenant()
    {
        Assert.Equal(Other, AdminTenantScope.ResolveRead(User(Platform, "Admin"), Other));
        Assert.Equal(Other, AdminTenantScope.ResolveRead(User(Platform, "SuperAdmin"), Other));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void PlatformAdmin_WithoutTarget_StaysOnOwnTenant(string? target)
    {
        Guid? requested = target == null ? null : Guid.Parse(target);
        Assert.Equal(Platform, AdminTenantScope.ResolveRead(User(Platform, "Admin"), requested));
    }

    [Fact]
    public void TenantAdmin_CannotReadAnotherTenant()
    {
        Assert.Equal(Other, AdminTenantScope.ResolveRead(User(Other, "Admin"), Platform));
    }

    [Fact]
    public void DemoTenantAdmin_CannotReadAnotherTenant()
    {
        Assert.Equal(Demo, AdminTenantScope.ResolveRead(User(Demo, "Admin"), Other));
    }

    [Fact]
    public void PlatformTenantWithoutAdminRole_CannotReadAnotherTenant()
    {
        Assert.Equal(Platform, AdminTenantScope.ResolveRead(User(Platform, "Tenant"), Other));
    }
}
