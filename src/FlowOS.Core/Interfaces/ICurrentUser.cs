using System;
using System.Collections.Generic;

namespace FlowOS.Core.Interfaces;

public interface ICurrentUser
{
    string? Id { get; }
    Guid TenantId { get; }
    List<string> Roles { get; }
    IReadOnlyCollection<string> Scopes { get; }
    bool IsApiKey { get; }

    /// <summary>
    /// Applications (4/4): the tenant application the caller's API key is restricted to, or null when the
    /// caller is unrestricted (a human session, an unrestricted key, or any non-API-key caller). Null
    /// never means "no application" for a restricted key - a key is either tied to exactly one
    /// application or not restricted at all (<see cref="FlowOS.Domain.Entities.TenantApiKey.IsApplicationRestricted"/>).
    /// </summary>
    Guid? ApplicationId { get; }
}
