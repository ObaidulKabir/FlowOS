using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.EndToEndTests.Concurrency;

/// <summary>
/// CR-10: two requests with the same idempotency key that arrive together must not both run,
/// and the loser must not fail with a database error.
/// </summary>
[Trait("Category", "Concurrency")]
public sealed class IdempotencyServiceConcurrencyTests : IClassFixture<PostgresConcurrencyFixture>
{
    private readonly PostgresConcurrencyFixture _fixture;

    public IdempotencyServiceConcurrencyTests(PostgresConcurrencyFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Concurrent_first_requests_with_the_same_key_let_exactly_one_begin()
    {
        var tenantId = Guid.NewGuid();
        var key = "race-" + Guid.NewGuid();
        var contexts = Enumerable.Range(0, 4).Select(_ => _fixture.CreateContext()).ToList();
        try
        {
            var attempts = contexts.Select(ctx => new IdempotencyService(ctx).TryBeginAsync(tenantId, "publish_event", key));

            var results = await Task.WhenAll(attempts);

            Assert.Equal(1, results.Count(started => started));
            await using var check = _fixture.CreateContext();
            Assert.Equal(1, await check.IdempotencyRecords.CountAsync(r => r.TenantId == tenantId && r.IdempotencyKey == key));
        }
        finally
        {
            foreach (var ctx in contexts)
                await ctx.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_failed_attempt_can_be_retried_with_the_same_key()
    {
        var tenantId = Guid.NewGuid();
        var key = "retry-" + Guid.NewGuid();

        await using (var ctx = _fixture.CreateContext())
        {
            var service = new IdempotencyService(ctx);
            Assert.True(await service.TryBeginAsync(tenantId, "publish_event", key));
            await service.FailAsync(tenantId, "publish_event", key, "conflict");
        }

        await using (var ctx = _fixture.CreateContext())
        {
            Assert.True(await new IdempotencyService(ctx).TryBeginAsync(tenantId, "publish_event", key));
        }
    }
}
