namespace FlowOS.Core.Security;

/// <summary>
/// Applications (4/4): whether a caller whose API key is restricted to one tenant application
/// (<see cref="FlowOS.Domain.Entities.TenantApiKey.IsApplicationRestricted"/>, surfaced as
/// <see cref="FlowOS.Core.Interfaces.ICurrentUser.ApplicationId"/>) may act on a resource that belongs to
/// a given application (or to none).
/// </summary>
public static class ApplicationScopeRules
{
    /// <summary>
    /// True when the caller may act on a resource whose own application is
    /// <paramref name="resourceApplicationId"/>. An unrestricted caller (<paramref name="callerApplicationId"/>
    /// null - a human session, an unrestricted key, or any non-API-key caller) may always act. A
    /// restricted caller may act only on a resource that names that exact same application; an
    /// unassigned resource (<paramref name="resourceApplicationId"/> null) is out of bounds for it, the
    /// same as a resource in a different application.
    /// </summary>
    public static bool Allows(Guid? callerApplicationId, Guid? resourceApplicationId) =>
        callerApplicationId is null || callerApplicationId == resourceApplicationId;
}
