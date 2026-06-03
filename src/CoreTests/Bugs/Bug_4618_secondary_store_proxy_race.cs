using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Marten;
using Marten.Internal;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

/// <summary>
/// Regression test for #4618: building the runtime proxy for the same ancillary-store
/// interface from multiple threads at once threw
/// "ArgumentException: Duplicate type name within an assembly".
/// <para>
/// <see cref="SecondaryStoreProxyFactory"/> cached the raw <see cref="Type"/> via
/// <c>ConcurrentDictionary.GetOrAdd</c>, whose value-factory is not guaranteed to run
/// only once under contention, so several threads could call
/// <c>ModuleBuilder.DefineType</c> with the same type name on the shared module and
/// collide. The race only fires on the FIRST build of a given interface, so this test
/// uses a fresh marker interface that no other test touches.
/// </para>
/// <para>
/// This exercises the factory directly rather than through <c>AddMartenStore</c>: it is
/// the exact code path that raced, reproduces reliably in milliseconds, and avoids
/// bootstrapping dozens of <c>DocumentStore</c>s. The public-API wiring
/// (<c>AddMartenStore</c> -&gt; <c>SecondaryStoreConfig.Build</c> -&gt; the factory) is
/// already covered by the existing single-threaded ancillary-store tests.
/// </para>
/// </summary>
public class Bug_4618_secondary_store_proxy_race
{
    // Fresh marker interface — must not be touched by any other test, or the factory
    // cache is already warm for that type and the first-build race cannot fire.
    public interface IRaceStore : IDocumentStore;

    [Fact]
    public void proxy_factory_is_thread_safe_on_first_build()
    {
        const int threads = 64;
        var exceptions = new ConcurrentBag<Exception>();
        using var ready = new Barrier(threads);

        var workers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
        {
            // Release all threads simultaneously to maximise the contention window.
            ready.SignalAndWait();
            try
            {
                SecondaryStoreProxyFactory.GetOrCreate(typeof(IRaceStore));
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }
        })).ToArray();

        foreach (var t in workers) t.Start();
        foreach (var t in workers) t.Join();

        exceptions.ShouldBeEmpty();
    }
}
