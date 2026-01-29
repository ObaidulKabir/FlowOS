using System;
using System.Collections.Generic;

namespace FlowOS.Application.Common.Interfaces;

public interface ICurrentUser
{
    string? Id { get; }
    Guid TenantId { get; }
    List<string> Roles { get; }
}
