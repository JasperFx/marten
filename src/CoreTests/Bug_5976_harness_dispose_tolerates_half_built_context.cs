using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
///     #5976. <c>OneOffConfigurationsContext.Dispose</c> threw a NullReferenceException under load in
///     Bug_1404_Hilo_concurrent_update_failure, which replaced the test's own result. That test calls
///     <c>SeparateStore()</c> from four threads at once, and each call added to an unsynchronized
///     <c>List&lt;IDisposable&gt;</c>. A <c>List.Add</c> that races a resize loses an item but still
///     bumps the count, so the list gains a null slot and the foreach in <c>Dispose()</c> hits it.
///     <c>IntegrationContext</c> and <c>StoreContext&lt;T&gt;</c> had the same list and the same
///     foreach, so every test here runs against all four base classes.
/// </summary>
public class Bug_5976_harness_dispose_tolerates_half_built_context
{
    public static IEnumerable<object[]> Contexts() =>
    [
        [nameof(OneOffProbe)],
        [nameof(BugProbe)],
        [nameof(IntegrationProbe)],
        [nameof(StoreContextProbe)]
    ];

    private static IProbe build(string kind) => kind switch
    {
        nameof(OneOffProbe) => new OneOffProbe(),
        nameof(BugProbe) => new BugProbe(),
        nameof(IntegrationProbe) => new IntegrationProbe(),
        nameof(StoreContextProbe) => new StoreContextProbe(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [MemberData(nameof(Contexts))]
    public void concurrent_adds_lose_nothing_and_dispose_never_throws(string kind)
    {
        // The real mechanism from Bug_1404: several threads adding to the context's disposables
        // at once. Eight threads x four items crosses the list's 4 -> 8 -> 16 -> 32 resizes.
        const int iterations = 300;
        const int threads = 8;
        const int perThread = 4;

        var failures = new List<string>();

        for (var i = 0; i < iterations; i++)
        {
            var probe = build(kind);
            var tracked = new Counting[threads * perThread];
            for (var j = 0; j < tracked.Length; j++) tracked[j] = new Counting();

            using (var gate = new Barrier(threads))
            {
                var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
                {
                    gate.SignalAndWait();
                    for (var k = 0; k < perThread; k++)
                    {
                        probe.Tracked.Add(tracked[t * perThread + k]);
                    }
                })).ToArray();

                foreach (var worker in workers) worker.Start();
                foreach (var worker in workers) worker.Join();
            }

            try
            {
                probe.Dispose();
            }
            catch (Exception e)
            {
                failures.Add($"iteration {i}: Dispose threw {e.GetType().Name}");
                continue;
            }

            var missed = tracked.Count(x => x.Disposals != 1);
            if (missed > 0)
            {
                failures.Add($"iteration {i}: {missed} disposable(s) were not disposed exactly once");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void a_null_entry_is_skipped(string kind)
    {
        var probe = build(kind);
        var before = new Counting();
        var after = new Counting();

        probe.Tracked.Add(before);
        probe.Tracked.Add(null);
        probe.Tracked.Add(after);

        Should.NotThrow(() => probe.Dispose());

        before.Disposals.ShouldBe(1);
        after.Disposals.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task a_context_that_never_built_anything_disposes_cleanly(string kind)
    {
        var probe = build(kind);

        Should.NotThrow(() => probe.Dispose());
        await Should.NotThrowAsync(async () => await probe.DisposeAsync());
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void one_failing_disposal_does_not_stop_the_rest_and_is_surfaced(string kind)
    {
        var probe = build(kind);
        var before = new Counting();
        var after = new Counting();

        probe.Tracked.Add(before);
        probe.Tracked.Add(new Throwing("first"));
        probe.Tracked.Add(after);

        // A single failure comes back as itself, not wrapped, so its stack trace reads as usual
        var ex = Should.Throw<InvalidOperationException>(() => probe.Dispose());
        ex.Message.ShouldBe("first");

        before.Disposals.ShouldBe(1);
        after.Disposals.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void several_failing_disposals_are_aggregated(string kind)
    {
        var probe = build(kind);
        var middle = new Counting();

        probe.Tracked.Add(new Throwing("first"));
        probe.Tracked.Add(middle);
        probe.Tracked.Add(new Throwing("second"));

        var ex = Should.Throw<AggregateException>(() => probe.Dispose());
        ex.InnerExceptions.Select(x => x.Message).ShouldBe(new[] { "first", "second" });

        middle.Disposals.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task disposing_twice_disposes_each_item_once(string kind)
    {
        var probe = build(kind);
        var tracked = Enumerable.Range(0, 10).Select(_ => new Counting()).ToArray();
        foreach (var x in tracked) probe.Tracked.Add(x);

        probe.Dispose();
        await probe.DisposeAsync();

        tracked.ShouldAllBe(x => x.Disposals == 1);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void racing_disposes_dispose_each_item_once(string kind)
    {
        const int iterations = 100;
        var failures = new List<string>();

        for (var i = 0; i < iterations; i++)
        {
            var probe = build(kind);
            var tracked = Enumerable.Range(0, 64).Select(_ => new Counting()).ToArray();
            foreach (var x in tracked) probe.Tracked.Add(x);

            using (var gate = new Barrier(2))
            {
                var workers = Enumerable.Range(0, 2).Select(_ => new Thread(() =>
                {
                    gate.SignalAndWait();
                    try
                    {
                        probe.Dispose();
                    }
                    catch (Exception e)
                    {
                        lock (failures) failures.Add($"iteration {i}: Dispose threw {e.GetType().Name}");
                    }
                })).ToArray();

                foreach (var worker in workers) worker.Start();
                foreach (var worker in workers) worker.Join();
            }

            var wrong = tracked.Count(x => x.Disposals != 1);
            if (wrong > 0)
            {
                failures.Add($"iteration {i}: {wrong} disposable(s) were not disposed exactly once");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void store_options_throwing_partway_leaves_a_disposable_context()
    {
        var probe = new OneOffProbe();
        var earlier = new Counting();
        probe.Tracked.Add(earlier);

        // The configure callback runs after the previous session is reset and before the new
        // store exists, so this is the half-built state the issue asks about.
        Should.Throw<DivideByZeroException>(() => probe.ConfigureStore(_ => throw new DivideByZeroException()));

        Should.NotThrow(() => probe.Dispose());
        earlier.Disposals.ShouldBe(1);
    }

    [Fact]
    public void store_options_throwing_after_a_store_and_session_were_built_still_disposes_them()
    {
        var probe = new OneOffProbe();

        var store = probe.ConfigureStore(_ => { });
        var session = probe.Session;

        Should.Throw<DivideByZeroException>(() => probe.ConfigureStore(_ => throw new DivideByZeroException()));

        // Both are still tracked after the failed reconfiguration, so both get disposed
        probe.Tracked.ShouldContain(store);
        probe.Tracked.ShouldContain(session);

        Should.NotThrow(() => probe.Dispose());
    }

    public interface IProbe: IDisposable, IAsyncDisposable
    {
        IList<IDisposable> Tracked { get; }
    }

    public class OneOffProbe: OneOffConfigurationsContext, IProbe
    {
        public IList<IDisposable> Tracked => Disposables;

        public Marten.DocumentStore ConfigureStore(Action<Marten.StoreOptions> configure) =>
            StoreOptions(configure, cleanAll: false);

        public Marten.IDocumentSession Session => theSession;
    }

    public class BugProbe: BugIntegrationContext, IProbe
    {
        public IList<IDisposable> Tracked => Disposables;
    }

    public class IntegrationProbe(): IntegrationContext(null), IProbe
    {
        public IList<IDisposable> Tracked => Disposables;
    }

    public class StoreContextProbe(): StoreContext<StoreFixture>(null), IProbe
    {
        public IList<IDisposable> Tracked => Disposables;
    }

    private class Counting: IDisposable
    {
        private int _disposals;
        public int Disposals => Volatile.Read(ref _disposals);
        public void Dispose() => Interlocked.Increment(ref _disposals);
    }

    private class Throwing(string message): IDisposable
    {
        public void Dispose() => throw new InvalidOperationException(message);
    }
}
