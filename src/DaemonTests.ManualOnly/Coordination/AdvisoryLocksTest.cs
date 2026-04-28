using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Daemon.Coordination;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.ManualOnly.Coordination;

public class AdvisoryLocksTest
{
    private static IAdvisoryLock CreateLock(IDocumentStore store, bool monitored)
    {
        var database = (MartenDatabase)store.Storage.Database;
        return AdvisoryLockFactory.Create(
            database.DataSource,
            NullLogger.Instance,
            database.Identifier,
            new AdvisoryLockOptions { LockMonitoringEnabled = monitored });
    }

    private static int IdFor(bool monitored, int seed) => (monitored ? 1000 : 2000) + seed;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task get_lock_smoke_test(bool monitored)
    {
        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        await using var locks = CreateLock(store, monitored);

        var lockId = IdFor(monitored, 50);

        locks.HasLock(lockId).ShouldBeFalse();

        (await locks.TryAttainLockAsync(lockId, CancellationToken.None)).ShouldBeTrue();

        locks.HasLock(lockId).ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task get_exclusive_lock(bool monitored)
    {
        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);

        await using var locks = CreateLock(store, monitored);
        await using var locks2 = CreateLock(store, monitored);

        var lockId = IdFor(monitored, 60);

        (await locks.TryAttainLockAsync(lockId, CancellationToken.None)).ShouldBeTrue();
        (await locks2.TryAttainLockAsync(lockId, CancellationToken.None)).ShouldBeFalse();

        await locks.ReleaseLockAsync(lockId);
        locks.HasLock(lockId).ShouldBeFalse();

        (await locks2.TryAttainLockAsync(lockId, CancellationToken.None)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task get_multiple_locks(bool monitored)
    {
        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        await using var locks = CreateLock(store, monitored);

        var first = IdFor(monitored, 70);
        var second = IdFor(monitored, 71);
        var third = IdFor(monitored, 72);

        (await locks.TryAttainLockAsync(first, CancellationToken.None)).ShouldBeTrue();
        (await locks.TryAttainLockAsync(second, CancellationToken.None)).ShouldBeTrue();
        (await locks.TryAttainLockAsync(third, CancellationToken.None)).ShouldBeTrue();

        locks.HasLock(first).ShouldBeTrue();
        locks.HasLock(second).ShouldBeTrue();
        locks.HasLock(third).ShouldBeTrue();

        await using var locks2 = CreateLock(store, monitored);
        (await locks2.TryAttainLockAsync(first, CancellationToken.None)).ShouldBeFalse();
        (await locks2.TryAttainLockAsync(second, CancellationToken.None)).ShouldBeFalse();
        (await locks2.TryAttainLockAsync(third, CancellationToken.None)).ShouldBeFalse();

        await locks.ReleaseLockAsync(first);
        locks.HasLock(first).ShouldBeFalse();

        locks.HasLock(second).ShouldBeTrue();
        locks.HasLock(third).ShouldBeTrue();

        (await locks2.TryAttainLockAsync(first, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task factory_returns_native_lock_when_monitoring_disabled()
    {
        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        await using var locks = CreateLock(store, monitored: false);

        locks.ShouldBeOfType<NativeAdvisoryLock>();
    }

    [Fact]
    public async Task factory_returns_monitored_lock_by_default()
    {
        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        await using var locks = CreateLock(store, monitored: true);

        locks.ShouldBeOfType<AdvisoryLock>();
    }
}
