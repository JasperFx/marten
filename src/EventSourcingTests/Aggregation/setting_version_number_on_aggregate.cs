using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class setting_version_number_on_aggregate : OneOffConfigurationsContext
{
    [Fact]
    public async Task set_on_live_aggregation()
    {
        StoreOptions(opts => opts.Projections.Add(new SampleSingleStream(), ProjectionLifecycle.Live));

        var stream = theSession.Events.StartStream(new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<MyAggregate>(stream.Id);
        aggregate.Version.ShouldBe(3);
        aggregate.ACount.ShouldBe(1);
    }

    [Fact]
    public async Task set_on_inline_single_stream_aggregation()
    {
        StoreOptions(opts => opts.Projections.Add(new SampleSingleStream(), ProjectionLifecycle.Inline));

        var stream = theSession.Events.StartStream(new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.LoadAsync<MyAggregate>(stream.Id);
        aggregate.Version.ShouldBe(3);
    }

    [Fact]
    public async Task set_on_async_single_stream_aggregation()
    {
        StoreOptions(opts => opts.Projections.Add(new SampleSingleStream(), ProjectionLifecycle.Async));

        var stream = theSession.Events.StartStream(new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await daemon.WaitForNonStaleData(5.Seconds());

        var aggregate = await theSession.LoadAsync<MyAggregate>(stream.Id);
        aggregate.Version.ShouldBe(3);
    }



    public class SampleSingleStream : SingleStreamProjection<MyAggregate, Guid>
    {
        public SampleSingleStream ()
        {
            ProjectionName = "AllGood";
        }

        [MartenIgnore]
        public void RandomMethodName()
        {

        }

        public MyAggregate Create(CreateEvent @event)
        {
            return new MyAggregate
            {
                ACount = @event.A,
                BCount = @event.B,
                CCount = @event.C,
                DCount = @event.D
            };
        }

        public static void Apply(AEvent @event, MyAggregate aggregate)
        {
            aggregate.ACount++;
        }

        public MyAggregate Apply(BEvent @event, MyAggregate aggregate)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount + 1,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount,
                Id = aggregate.Id
            };
        }

        public void Apply(MyAggregate aggregate, CEvent @event)
        {
            aggregate.CCount++;
        }

        public MyAggregate Apply(MyAggregate aggregate, DEvent @event)
        {
            return new MyAggregate
            {
                ACount = aggregate.ACount,
                BCount = aggregate.BCount,
                CCount = aggregate.CCount,
                DCount = aggregate.DCount + 1,
                Id = aggregate.Id
            };
        }
    }

    [Fact]
    public async Task set_version_on_aggregate_with_explicit_Version_attribute()
    {
        StoreOptions(opts => opts.Projections.Snapshot<MyAggregateWithDifferentVersionProperty>(SnapshotLifecycle.Inline));

        var stream = theSession.Events.StartStream(new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.LoadAsync<MyAggregateWithDifferentVersionProperty>(stream.Id);
        aggregate.SpecialVersion.ShouldBe(3);
    }

    public class MyAggregateWithDifferentVersionProperty
    {
        [Version]
        public int SpecialVersion { get; set; }


        public Guid Id { get; set; }

        public int ACount { get; set; }


        public void Apply(AEvent evt) => ACount++;
    }


}


