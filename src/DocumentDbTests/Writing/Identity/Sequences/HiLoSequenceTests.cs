using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
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
    private static readonly Func<HiloSequence, Task> SyncNext = sequence =>  {
        sequence.AdvanceToNextHiSync();
        return Task.CompletedTask;
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

public class HiloSequenceTests : IntegrationContext
{

    private readonly HiloSequence theSequence;
    private Tenant theTenant;

    public HiloSequenceTests(DefaultStoreFixture fixture) : base(fixture)
    {
        theStore.Advanced.Clean.CompletelyRemoveAll();

        theTenant = theStore.Tenancy.Default;

        theSequence = theTenant.Database.Sequences.SequenceFor(typeof(Foo)).As<HiloSequence>();
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
        for (var i = 0; i < 5000; i++)
        {
            theSequence.NextLong().ShouldBe(i + 1);
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
    public void is_thread_safe()
    {
        var tasks = new Task<List<int>>[] {startThread(), startThread(), startThread(), startThread(), startThread(), startThread()};

        Task.WaitAll(tasks);

        var all = tasks.SelectMany(x => x.Result).ToArray();

        all.GroupBy(x => x).Any(x => x.Count() > 1).ShouldBeFalse();

        all.Distinct().Count().ShouldBe(tasks.Length * 1000);
    }
}