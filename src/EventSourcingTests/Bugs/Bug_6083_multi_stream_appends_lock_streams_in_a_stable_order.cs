#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events.Operations;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #6083. A <c>SaveChangesAsync</c> that appends to several streams locked their <c>mt_streams</c> rows in
/// the order the session appended to them. Two sessions appending to the same streams in opposite orders
/// could each lock one row and wait for the other's, and PostgreSQL failed one of them with a deadlock
/// (40P01). The unit of work now locks every existing stream row it appends to up front, ordered by stream
/// id and then tenant, before any append runs.
/// </summary>
public class Bug_6083_multi_stream_appends_lock_streams_in_a_stable_order: OneOffConfigurationsContext
{
    private const int WritersPerDirection = 3;
    private const int SavesPerWriter = 25;

    private const string Red = "red";
    private const string Blue = "blue";

    public enum Tenancy
    {
        Single,

        // Two streams in one tenant
        Conjoined,

        // One stream id in two tenants, so the rows only differ by tenant
        ConjoinedAcrossTenants
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in new[] { EventAppendMode.Quick, EventAppendMode.Rich })
        foreach (var expectedVersions in new[] { false, true })
        foreach (var tenancy in Enum.GetValues<Tenancy>())
        {
            yield return [mode, expectedVersions, tenancy, StreamIdentity.AsGuid];
        }

        foreach (var mode in new[] { EventAppendMode.Quick, EventAppendMode.Rich })
        foreach (var expectedVersions in new[] { false, true })
        {
            yield return [mode, expectedVersions, Tenancy.Single, StreamIdentity.AsString];
            yield return [mode, expectedVersions, Tenancy.ConjoinedAcrossTenants, StreamIdentity.AsString];
        }

        yield return [EventAppendMode.QuickWithServerTimestamps, false, Tenancy.Single, StreamIdentity.AsGuid];
        yield return [EventAppendMode.QuickWithServerTimestamps, true, Tenancy.Conjoined, StreamIdentity.AsGuid];
    }

    private record Target(string? TenantId, object Id);

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task sessions_appending_to_the_same_streams_in_opposite_orders_never_deadlock(
        EventAppendMode mode, bool expectedVersions, Tenancy tenancy, StreamIdentity identity)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = mode;
            opts.Events.StreamIdentity = identity;

            if (tenancy != Tenancy.Single)
            {
                opts.Events.TenancyStyle = TenancyStyle.Conjoined;
                opts.Policies.AllDocumentsAreMultiTenanted();
            }

            // No write retries, so a deadlock (40P01) fails the test instead of committing on a retry
            opts.ConfigureWritePolly(_ => { });
        });

        var ct = TestContext.Current.CancellationToken;

        object newId() => identity == StreamIdentity.AsGuid ? Guid.NewGuid() : "stream-" + Guid.NewGuid();

        var first = newId();
        Target[] targets = tenancy switch
        {
            Tenancy.Single => [new Target(null, first), new Target(null, newId())],
            Tenancy.Conjoined => [new Target(Red, first), new Target(Red, newId())],
            _ => [new Target(Red, first), new Target(Blue, first)]
        };

        foreach (var target in targets)
        {
            await using var session = openSession(target.TenantId);
            if (target.Id is Guid id)
            {
                session.Events.StartStream<LockOrderTally>(id, new LockOrderMoved(-1));
            }
            else
            {
                session.Events.StartStream<LockOrderKeyedTally>((string)target.Id, new LockOrderMoved(-1));
            }

            await session.SaveChangesAsync(ct);
        }

        var failures = new ConcurrentQueue<string>();
        var saved = 0;

        async Task writeAsync(int writer, Target[] order)
        {
            var done = 0;
            for (var attempt = 0; done < SavesPerWriter && attempt < SavesPerWriter * 50; attempt++)
            {
                try
                {
                    await using var session = openSession(order[0].TenantId);
                    foreach (var target in order)
                    {
                        var events = target.TenantId == null || tenancy == Tenancy.Single
                            ? session.Events
                            : session.ForTenant(target.TenantId).Events;

                        await appendAsync(events, target, new LockOrderMoved(writer), expectedVersions, ct);
                    }

                    await session.SaveChangesAsync(ct);
                    done++;
                    Interlocked.Increment(ref saved);
                }
                catch (Exception e) when (e is ConcurrencyException or EventStreamUnexpectedMaxEventIdException)
                {
                    // Another writer appended first. Try again
                }
                catch (Exception e)
                {
                    failures.Enqueue($"Writer {writer}: {e}");
                    return;
                }
            }

            if (done < SavesPerWriter) failures.Enqueue($"Writer {writer} only saved {done} times");
        }

        var reversed = targets.Reverse().ToArray();
        var writers = Enumerable.Range(0, WritersPerDirection * 2)
            .Select(w => Task.Run(() => writeAsync(w, w % 2 == 0 ? targets : reversed), ct));
        await Task.WhenAll(writers);

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
        saved.ShouldBe(WritersPerDirection * 2 * SavesPerWriter);

        // Every save appended to each stream exactly once, at consecutive versions
        var expected = Enumerable.Range(1, 1 + saved).Select(x => (long)x).ToArray();
        foreach (var target in targets)
        {
            await using var query = openSession(target.TenantId);
            var events = target.Id is Guid id
                ? await query.Events.FetchStreamAsync(id, token: ct)
                : await query.Events.FetchStreamAsync((string)target.Id, token: ct);

            events.Select(x => x.Version).ShouldBe(expected);

            var state = target.Id is Guid id2
                ? await query.Events.FetchStreamStateAsync(id2, ct)
                : await query.Events.FetchStreamStateAsync((string)target.Id, ct);
            state!.Version.ShouldBe(1 + saved);
        }
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.Rich)]
    public async Task the_up_front_lock_is_only_queued_for_more_than_one_stream(EventAppendMode mode)
    {
        var listener = new OperationsListener();
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = mode;
            opts.Listeners.Add(listener);
        });

        var ct = TestContext.Current.CancellationToken;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<LockOrderTally>(a, new LockOrderMoved(-1));
            session.Events.StartStream<LockOrderTally>(b, new LockOrderMoved(-1));
            await session.SaveChangesAsync(ct);
        }

        // The two new streams have no rows to lock yet, but both are named in the lock
        listener.Locks.ShouldBe(1);

        // One stream, even with several appends to it: no extra statement
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(a, new LockOrderMoved(0));
            session.Events.Append(a, new LockOrderMoved(1));
            await session.SaveChangesAsync(ct);
        }

        listener.Locks.ShouldBe(0);

        // Two streams: one lock, ahead of every append
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(b, new LockOrderMoved(0));
            session.Events.Append(a, new LockOrderMoved(0));
            await session.SaveChangesAsync(ct);
        }

        listener.Locks.ShouldBe(1);
        listener.LockIsFirst.ShouldBeTrue();
    }

    private class OperationsListener: DocumentSessionListenerBase
    {
        public int Locks { get; private set; }
        public bool LockIsFirst { get; private set; }

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            var operations = ((Marten.Internal.Sessions.DocumentSessionBase)session).WorkTracker.AllOperations;
            Locks = operations.Count(x => x is LockStreamsForAppend);
            LockIsFirst = operations.Count > 0 && operations[0] is LockStreamsForAppend;
            return Task.CompletedTask;
        }
    }

    private IDocumentSession openSession(string? tenantId)
        => tenantId == null ? theStore.LightweightSession() : theStore.LightweightSession(tenantId);

    private static async Task appendAsync(IEventStoreOperations events, Target target, LockOrderMoved moved,
        bool expectedVersions, CancellationToken ct)
    {
        switch (target.Id)
        {
            case Guid id when expectedVersions:
                (await events.FetchForWriting<LockOrderTally>(id, ct)).AppendOne(moved);
                break;
            case Guid id:
                events.Append(id, moved);
                break;
            case string key when expectedVersions:
                (await events.FetchForWriting<LockOrderKeyedTally>(key, ct)).AppendOne(moved);
                break;
            case string key:
                events.Append(key, moved);
                break;
        }
    }
}

public record LockOrderMoved(int Writer);

public class LockOrderTally
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(LockOrderMoved _) => Count++;
}

public class LockOrderKeyedTally
{
    public string Id { get; set; } = "";
    public int Count { get; set; }

    public void Apply(LockOrderMoved _) => Count++;
}
