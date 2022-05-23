using System.Threading.Tasks;
using Baseline.Dates;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
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
        await daemon.StartAllShards();

        await daemon.WaitForNonStaleData(5.Seconds());

        var aggregate = await theSession.LoadAsync<MyAggregate>(stream.Id);
        aggregate.Version.ShouldBe(3);
    }

    public class SampleSingleStream : SingleStreamAggregation<MyAggregate>
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

        public void Apply(AEvent @event, MyAggregate aggregate)
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

    /*
 On live aggregation of one stream
 On inline aggregation of one stream
 On inline aggregation through MultiStreamAggregation (uses farthest sequence encountered)
 On async aggregation of one stream
 On async aggregation through MultiStreamAggregation (uses farthest sequence encountered)
     */
}


