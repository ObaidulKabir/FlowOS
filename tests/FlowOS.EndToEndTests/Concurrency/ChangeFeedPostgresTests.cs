using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence.Configurations;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.EndToEndTests.Concurrency;

/// <summary>
/// CR-10c: on PostgreSQL the change-feed sequence comes from the identity column added by the
/// migration, and the reader pages by it.
/// </summary>
[Trait("Category", "Concurrency")]
public sealed class ChangeFeedPostgresTests : IClassFixture<PostgresConcurrencyFixture>
{
    private readonly PostgresConcurrencyFixture _fixture;

    public ChangeFeedPostgresTests(PostgresConcurrencyFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Database_assigns_increasing_sequences_and_the_reader_pages_by_them()
    {
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();

        // Separate contexts and saves, like separate requests.
        for (var i = 0; i < 5; i++)
        {
            await using var ctx = _fixture.CreateContext();
            ctx.Events.Add(new StandardEvent(tenantId, $"EVT-PG-{i}"));
            ctx.Events.Add(new StandardEvent(otherTenant, $"EVT-PG-OTHER-{i}"));
            await ctx.SaveChangesAsync();
        }

        await using var read = _fixture.CreateContext();
        var stored = await read.Events.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Select(e => new { e.EventType, Sequence = EF.Property<long>(e, EventConfiguration.SequenceProperty) })
            .OrderBy(e => e.Sequence)
            .ToListAsync();
        Assert.Equal(5, stored.Count);
        Assert.All(stored, e => Assert.True(e.Sequence > 0));
        Assert.Equal(Enumerable.Range(0, 5).Select(i => $"EVT-PG-{i}"), stored.Select(e => e.EventType));

        var reader = new ChangeFeedReader(read);
        var visibleBefore = DateTime.UtcNow.AddMinutes(1);
        var first = await reader.ReadAsync(tenantId, 0, 2, visibleBefore);
        var second = await reader.ReadAsync(tenantId, first.LastSequence, 2, visibleBefore);
        var third = await reader.ReadAsync(tenantId, second.LastSequence, 2, visibleBefore);

        Assert.True(first.HasMore);
        Assert.True(second.HasMore);
        Assert.False(third.HasMore);
        var walked = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.EventType).ToList();
        Assert.Equal(stored.Select(e => e.EventType), walked);
        Assert.DoesNotContain(walked, t => t.Contains("OTHER"));
    }

    [Fact]
    public async Task Reader_stops_at_the_first_event_inside_the_settle_window()
    {
        var tenantId = Guid.NewGuid();
        await using (var ctx = _fixture.CreateContext())
        {
            ctx.Events.Add(new StandardEvent(tenantId, "EVT-SETTLE-0"));
            await ctx.SaveChangesAsync();
        }

        await using var read = _fixture.CreateContext();
        var page = await new ChangeFeedReader(read).ReadAsync(tenantId, 0, 10, DateTime.UtcNow.AddMinutes(-10));

        Assert.Empty(page.Items);
        Assert.True(page.HasMore);
        Assert.Equal(0, page.LastSequence);
    }
}
