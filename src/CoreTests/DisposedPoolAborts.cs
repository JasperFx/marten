using System;
using System.Diagnostics;

namespace CoreTests;

/// <summary>
/// Shared detector for the one abort that #4874 and #4915 are about: the projection coordinator's leadership
/// poll re-opening a connection against an already-disposed Npgsql pool.
/// </summary>
/// <remarks>
/// Both regression tests hook <c>AppDomain.CurrentDomain.FirstChanceException</c>, which is AppDomain-wide.
/// Counting every disposed-pool <see cref="ObjectDisposedException"/> in the process therefore sweeps up noise
/// that has nothing to do with either bug:
///
///   * other tests leak background work -- daemon StopAllAsync, schema migrations, session reads -- that
///     touches their own disposed data sources at arbitrary later times, and
///   * <c>Weasel.Postgresql.AdvisoryLock.DisposeAsync</c> benignly releases a lock handle whose data source
///     already went away, which Weasel swallows by design.
///
/// CoreTests runs serially, so this is not a parallelism problem and cannot be fixed with a collection
/// attribute. Attribute the abort to the poll instead: <c>TryAttainLockAsync</c> touching a dead pool is the
/// bug; anything else touching one is not. The exception's own <see cref="Exception.StackTrace"/> is not yet
/// populated when the first-chance hook runs, so walk the throwing thread's live stack.
/// </remarks>
public static class DisposedPoolAborts
{
    public static bool IsAdvisoryLockPollAbort(Exception exception)
    {
        if (exception is not ObjectDisposedException ode) return false;

        if (ode.ObjectName?.Contains("PoolingDataSource") != true &&
            !ode.Message.Contains("PoolingDataSource"))
        {
            return false;
        }

        return new StackTrace(false).ToString().Contains("TryAttainLockAsync", StringComparison.Ordinal);
    }
}
