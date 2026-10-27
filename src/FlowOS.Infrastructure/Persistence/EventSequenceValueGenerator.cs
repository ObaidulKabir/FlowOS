using System.Threading;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace FlowOS.Infrastructure.Persistence;

/// <summary>
/// Assigns the change-feed sequence for providers without an identity column (the in-memory
/// provider used in tests). PostgreSQL uses its identity column instead.
/// </summary>
public sealed class EventSequenceValueGenerator : ValueGenerator<long>
{
    private static long _last;

    public override bool GeneratesTemporaryValues => false;

    public override long Next(EntityEntry entry) => Interlocked.Increment(ref _last);
}
