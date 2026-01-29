using System;

namespace FlowOS.Application.Common.Interfaces;

/// <summary>
/// Marker interface for commands that require policy enforcement.
/// </summary>
public interface IPolicySecuredCommand
{
    Guid TenantId { get; }
}
