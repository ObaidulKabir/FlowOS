using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlowOS.Core.Common.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.Infrastructure.Services.Security;
using FlowOS.Security.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowOS.UnitTests.Time;

/// <summary>
/// CR-08: time-based decisions follow the injected clock, so tests move time instead of waiting.
/// </summary>
public class ClockControlledBehaviourTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Timer_fires_only_after_the_clock_reaches_its_due_time()
    {
        var clock = new ManualTimeProvider(Monday);
        var options = new DbContextOptionsBuilder<FlowOSDbContext>().UseInMemoryDatabase("timers_" + Guid.NewGuid()).Options;
        await using var db = new FlowOSDbContext(options);
        using var services = new ServiceCollection().BuildServiceProvider();
        var timers = new WorkflowTimerService(db, services, NullLogger<WorkflowTimerService>.Instance, clock);

        await timers.ScheduleTimerAsync(Guid.NewGuid(), Guid.NewGuid(), "Review", TimeSpan.FromHours(48), "EVT-SLA-TIMEOUT");

        var job = await db.WorkflowTimerJobs.SingleAsync();
        Assert.Equal(Monday.UtcDateTime.AddHours(48), job.DueTimeUtc);

        clock.Advance(TimeSpan.FromHours(47));
        Assert.Equal(0, await timers.ExecuteDueTimersAsync());

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, await timers.ExecuteDueTimersAsync());
        Assert.True((await db.WorkflowTimerJobs.SingleAsync()).IsProcessed);
    }

    [Fact]
    public void Allowed_days_policy_follows_the_clock()
    {
        var clock = new ManualTimeProvider(Monday);
        var evaluator = new DefaultPolicyEvaluator(clock);
        var policy = new Policy { Name = "WeekdaysOnly", ConditionJson = "{\"allowedDays\":[\"Monday\",\"Tuesday\"]}" };

        Assert.True(evaluator.Evaluate(policy, new PolicyContext()).IsAllowed);

        clock.Advance(TimeSpan.FromDays(6)); // Sunday
        var sunday = evaluator.Evaluate(policy, new PolicyContext());
        Assert.False(sunday.IsAllowed);
        Assert.Contains("Sunday", sunday.Reason);
    }

    [Fact]
    public void Webhook_signature_tolerance_follows_the_clock()
    {
        var clock = new ManualTimeProvider(Monday);
        var service = new WebhookSignatureService(clock);
        var header = service.FormatSignatureHeader("secret", "{}", Monday.ToUnixTimeSeconds());

        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.True(service.VerifySignature("secret", "{}", header));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(service.VerifySignature("secret", "{}", header));
    }

    [Fact]
    public void Jwt_expires_when_the_clock_passes_its_lifetime()
    {
        var clock = new ManualTimeProvider(Monday);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["FlowOS:Auth:JwtSecret"] = "clock-test-secret-that-is-at-least-32-characters"
        }).Build();
        var jwt = new JwtTokenService(config, NullLogger<JwtTokenService>.Instance, clock);
        var token = jwt.GenerateToken(Guid.NewGuid(), "a@example.org", "A", Guid.NewGuid(), "T", "Admin", lifetime: TimeSpan.FromHours(1));

        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.NotNull(jwt.ValidateToken(token));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(jwt.ValidateToken(token));
    }
}
