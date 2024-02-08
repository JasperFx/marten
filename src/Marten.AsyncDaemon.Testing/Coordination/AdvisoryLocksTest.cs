using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.Coordination;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing.Coordination;

public class AdvisoryLocksTest
{
    [Fact]
    public async Task get_lock_smoke_test()
    {
        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);

        await using var locks = new AdvisoryLock(store.Storage.Database, NullLogger.Instance);

        locks.HasLock(50).ShouldBeFalse();

        (await locks.TryAttainLockAsync(50, CancellationToken.None)).ShouldBeTrue();

        locks.HasLock(50).ShouldBeTrue();
    }

    [Fact]
    public async Task get_exclusive_lock()
    {
        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);

        await using var locks = new AdvisoryLock(store.Storage.Database, NullLogger.Instance);
        await using var locks2 = new AdvisoryLock(store.Storage.Database, NullLogger.Instance);

        (await locks.TryAttainLockAsync(50, CancellationToken.None)).ShouldBeTrue();
        (await locks2.TryAttainLockAsync(50, CancellationToken.None)).ShouldBeFalse();

        await locks.ReleaseLockAsync(50);
        locks.HasLock(50).ShouldBeFalse();

        (await locks2.TryAttainLockAsync(50, CancellationToken.None)).ShouldBeTrue();

    }

    [Fact]
    public async Task get_multiple_locks()
    {
        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);

        await using var locks = new AdvisoryLock(store.Storage.Database, NullLogger.Instance);

        (await locks.TryAttainLockAsync(50, CancellationToken.None)).ShouldBeTrue();
        (await locks.TryAttainLockAsync(51, CancellationToken.None)).ShouldBeTrue();
        (await locks.TryAttainLockAsync(52, CancellationToken.None)).ShouldBeTrue();

        locks.HasLock(50).ShouldBeTrue();
        locks.HasLock(51).ShouldBeTrue();
        locks.HasLock(52).ShouldBeTrue();

        await using var locks2 = new AdvisoryLock(store.Storage.Database, NullLogger.Instance);
        (await locks2.TryAttainLockAsync(50, CancellationToken.None)).ShouldBeFalse();
        (await locks2.TryAttainLockAsync(51, CancellationToken.None)).ShouldBeFalse();
        (await locks2.TryAttainLockAsync(52, CancellationToken.None)).ShouldBeFalse();

        await locks.ReleaseLockAsync(50);
        locks.HasLock(50).ShouldBeFalse();

        locks.HasLock(51).ShouldBeTrue();
        locks.HasLock(52).ShouldBeTrue();

        (await locks2.TryAttainLockAsync(50, CancellationToken.None)).ShouldBeTrue();

    }
}
