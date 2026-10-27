using System;

namespace FlowOS.UnitTests.Time;

/// <summary>A clock that only moves when a test moves it (CR-08).</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);

    public void Set(DateTimeOffset at) => _now = at;
}
