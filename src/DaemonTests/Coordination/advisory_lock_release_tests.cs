using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Coordination;

/// <summary>
/// #5567. Covers the release behaviour of <see cref="AdvisoryLock" />, whose
/// <see cref="AdvisoryLock.DisposeAsync" /> releases concurrently and bounds how long it waits.
/// </summary>
/// <remarks>
/// #5572 vendored that class into Marten to make this fix without a two-repository release chain;
/// weasel#676 took it upstream and #5574 deleted the copy, so this now exercises
/// <c>Weasel.Postgresql</c>'s type again. Weasel carries an equivalent of this file, and it is kept
/// here as well on purpose: the configuration under test is the one Marten's
/// <see cref="Marten.Events.Daemon.Coordination.ProjectionCoordinator" /> actually builds, feeding
/// <c>ReleaseTimeout</c> from <c>Projections.StopAndDrainTimeout</c>, which is Marten's wiring to
/// keep honest rather than Weasel's.
/// <para>
/// The bound itself -- that a stop cannot exceed <c>Projections.StopAndDrainTimeout</c> no matter
/// what the connection monitor is doing -- is covered behaviourally by
/// <see cref="Bug_5567_hotcold_stop_stalls_behind_monitored_lock" />, because making a real release
/// hang on demand would mean reaching inside Medallion's connection monitor. What is pinned here is
/// everything that has to stay true either side of that bound: the locks are actually released, and
/// the opt-out convention Marten inherits from <c>DaemonSettings.StopAndDrainTimeout</c> is honoured
/// rather than read as "give up immediately".
/// </para>
/// <para>
/// Only the negative-budget case of that theory fails without the opt-out guard, and it is worth
/// knowing why, because the other two look like they should and do not. A zero or infinite budget is
/// indistinguishable here either way: the releases finish in milliseconds on a healthy connection,
/// so by the time the assertions run the locks are off the session whether <c>DisposeAsync</c>
/// waited for them or abandoned them. A negative budget is different in kind -- <c>Task.Delay</c>
/// throws <see cref="ArgumentOutOfRangeException" /> on anything negative except
/// <see cref="Timeout.InfiniteTimeSpan" /> -- so that case fails outright without the guard, which
/// is what makes it the one with teeth.
/// </para>
/// </remarks>
public class advisory_lock_release_tests
{
    private const int FirstLockId = 5567_0001;
    private const int LockCount = 8;

    private static AdvisoryLock lockFor(IDocumentStore store, TimeSpan releaseTimeout) =>
        new(((MartenDatabase)store.Storage.Database).DataSource,
            NullLogger.Instance,
            store.Storage.Database.Identifier,
            new AdvisoryLockOptions
            {
                // The combination that used to stall: session-scoped, multiplexed, monitored.
                LockMonitoringEnabled = true,
                TransactionalLockEnabled = false,
                ReleaseTimeout = releaseTimeout
            });

    [Theory]
    [InlineData(5)]    // the ordinary bounded case
    [InlineData(0)]    // opts out of the bound -- must still release, not give up immediately
    [InlineData(-1)]   // Timeout.InfiniteTimeSpan, same opt-out
    [InlineData(-5)]   // a negative budget: Task.Delay would throw on this if it reached it
    public async Task dispose_releases_every_held_lock(int releaseTimeoutSeconds)
    {
        var releaseTimeout = releaseTimeoutSeconds switch
        {
            -1 => Timeout.InfiniteTimeSpan,
            _ => releaseTimeoutSeconds.Seconds()
        };

        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);

        var locks = lockFor(store, releaseTimeout);

        for (var i = 0; i < LockCount; i++)
        {
            (await locks.TryAttainLockAsync(FirstLockId + i, CancellationToken.None)).ShouldBeTrue();
        }

        // Reading HasLock is what activates Medallion's connection monitor, which is the whole
        // reason the release path needed changing -- so the release has to be exercised with
        // monitoring live, not just with the locks held.
        for (var i = 0; i < LockCount; i++)
        {
            locks.HasLock(FirstLockId + i).ShouldBeTrue();
        }

        await locks.DisposeAsync();

        // A second lock instance can only take these if the first really gave them up. With an
        // opt-out timeout that is true the moment DisposeAsync returns; with a bound it is true
        // once the releases finish, which for a healthy connection is well inside the bound.
        await using var other = lockFor(store, 5.Seconds());
        for (var i = 0; i < LockCount; i++)
        {
            (await other.TryAttainLockAsync(FirstLockId + i, CancellationToken.None))
                .ShouldBeTrue($"lock {FirstLockId + i} was not released by DisposeAsync");
        }
    }

    [Fact]
    public async Task dispose_with_nothing_held_is_a_no_op()
    {
        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);

        var locks = lockFor(store, 5.Seconds());

        // Must not fault, and must not wait out the release timeout for zero handles
        await locks.DisposeAsync();
    }

    [Fact]
    public async Task releasing_one_lock_leaves_the_others_held()
    {
        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);

        await using var locks = lockFor(store, 5.Seconds());

        (await locks.TryAttainLockAsync(FirstLockId + 20, CancellationToken.None)).ShouldBeTrue();
        (await locks.TryAttainLockAsync(FirstLockId + 21, CancellationToken.None)).ShouldBeTrue();

        await locks.ReleaseLockAsync(FirstLockId + 20);

        locks.HasLock(FirstLockId + 20).ShouldBeFalse();
        locks.HasLock(FirstLockId + 21).ShouldBeTrue();
    }
}
