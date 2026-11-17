using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema.Identity.Sequences;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

public class Foo
{
    public int Id;
}

public class AdvanceToNextHi : IEnumerable<object[]>
{
    private static readonly Func<HiloSequence, Task> AsyncNext = sequence => sequence.AdvanceToNextHi();
    private static readonly Func<HiloSequence, Task> SyncNext = async sequence =>  {
        // #5862: every advance starts prefetching the following block in the background. Let it land
        // first, so the synchronous advance deterministically installs the next hi rather than racing a
        // fallback fetch against the prefetch (the fallback is covered by hilo_prefetch_tests).
        await sequence.WaitForPrefetchAsync();

        // AdvanceToNextHiSync is protected on the lifted Weasel.Core HiloSequenceBase (#4527),
        // so invoke it reflectively to keep exercising the synchronous hi-advance path.
        typeof(HiloSequence)
            .GetMethod("AdvanceToNextHiSync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sequence, null);
    };
    public IEnumerator<object[]> GetEnumerator()
    {
        yield return new object []{ AsyncNext };
        yield return new object[] { SyncNext };
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

// #5935: blocks thread-pool threads on purpose, so it must not run beside other tests
[Collection(HiloThreadPoolCollection.Name)]
public class hilo_tests : OneOffConfigurationsContext, IAsyncLifetime
{
    private HiloSequence theSequence;
    private Tenant theTenant;

    public override async ValueTask InitializeAsync()
    {
        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();
        theTenant = theStore.Tenancy.Default;

        theSequence = theTenant.Database.Sequences.SequenceFor(typeof(Foo)).As<HiloSequence>();
    }

    public override async ValueTask DisposeAsync()
    {
        // #5862: a prefetch still in flight would reserve a hi in this schema after the next test has
        // reset it, and that test asserts exact hi values
        if (theSequence != null) await theSequence.WaitForPrefetchAsync();

        Dispose();
        await base.DisposeAsync();
    }

    [Fact]
    public void default_values()
    {
        theSequence.CurrentHi.ShouldBe(-1);
        theSequence.MaxLo.ShouldBe(1000);
    }

    [Fact]
    public void should_advance_initial_case()
    {
        theSequence.ShouldAdvanceHi().ShouldBeTrue();
    }

    [Theory]
    [ClassData(typeof(AdvanceToNextHi))]
    public async Task advance_to_next_hi_from_initial_state(Func<HiloSequence, Task> advanceToNextHi)
    {
        await advanceToNextHi(theSequence);

        theSequence.CurrentLo.ShouldBe(1);
        theSequence.CurrentHi.ShouldBe(0);
    }

    [Theory]
    [ClassData(typeof(AdvanceToNextHi))]
    public async Task advance_to_next_hi_several_times(Func<HiloSequence, Task> advanceToNextHi)
    {
        await advanceToNextHi(theSequence);

        await advanceToNextHi(theSequence);
        theSequence.CurrentHi.ShouldBe(1);

        await advanceToNextHi(theSequence);
        theSequence.CurrentHi.ShouldBe(2);

        await advanceToNextHi(theSequence);
        theSequence.CurrentHi.ShouldBe(3);
    }

    [Theory]
    [ClassData(typeof(AdvanceToNextHi))]
    public async Task advance_value_from_initial_state(Func<HiloSequence, Task> advanceToNextHi)
    {
        // Gotta do this at least once
        await advanceToNextHi(theSequence);

        theSequence.AdvanceValue().ShouldBe(1);
        theSequence.AdvanceValue().ShouldBe(2);
        theSequence.AdvanceValue().ShouldBe(3);
        theSequence.AdvanceValue().ShouldBe(4);
        theSequence.AdvanceValue().ShouldBe(5);
    }

    [Fact]
    public void read_from_a_single_thread_from_0_to_5000()
    {
        // #5862: ids are unique and increasing, but no longer guaranteed contiguous across a block
        // boundary. A tight loop exhausts a block faster than its prefetch lands, and when the
        // synchronous fallback overtakes the prefetch, the prefetched block is skipped (a gap).
        var previous = 0L;
        for (var i = 0; i < 5000; i++)
        {
            var next = theSequence.NextLong();
            if (i == 0) next.ShouldBe(1);
            next.ShouldBeGreaterThan(previous);
            previous = next;
        }
    }

    private Task<List<int>> startThread()
    {
        return Task.Factory.StartNew(() =>
        {
            var list = new List<int>();

            for (int i = 0; i < 1000; i++)
            {
                list.Add(theSequence.NextInt());
            }

            return list;
        });
    }

    [Fact]
    public async Task is_thread_safe()
    {
        var tasks = new Task<List<int>>[] {startThread(), startThread(), startThread(), startThread(), startThread(), startThread()};

        await Task.WhenAll(tasks);

        var all = tasks.SelectMany(x => x.Result).ToArray();

        all.GroupBy(x => x).Any(x => x.Count() > 1).ShouldBeFalse();

        all.Distinct().Count().ShouldBe(tasks.Length * 1000);
    }
}
