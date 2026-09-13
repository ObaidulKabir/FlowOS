using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Models;

namespace FlowOS.Core.Common.Interfaces;

public interface IDeadLetterService
{
    Task<IReadOnlyList<DeadLetterMessageDto>> GetDeadLettersAsync(
        Guid? tenantId,
        string? typeFilter = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);

    Task<DeadLetterMessageDto?> GetDeadLetterByIdAsync(
        Guid messageId,
        Guid? tenantId = null,
        CancellationToken ct = default);

    Task<bool> RetryDeadLetterAsync(
        Guid messageId,
        Guid? tenantId = null,
        CancellationToken ct = default);

    Task<int> RetryAllDeadLettersAsync(
        Guid? tenantId = null,
        string? typeFilter = null,
        CancellationToken ct = default);

    Task<bool> PurgeDeadLetterAsync(
        Guid messageId,
        Guid? tenantId = null,
        CancellationToken ct = default);
}
