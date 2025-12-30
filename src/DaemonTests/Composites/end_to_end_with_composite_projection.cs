using System.Linq;
using System.Threading.Tasks;
using DaemonTests.Aggregations;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using Marten;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Composites;

public class end_to_end_with_composite_projection : DaemonContext
{
    public end_to_end_with_composite_projection(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task run_with_multiple_projections()
    {
        StoreOptions(opts =>
        {
            // This is running two (could be more) projections together, so...
            // using the same loaded events and...
            // persisting in the same batch of updates for...
            // fewer network round trips
            opts.Projections.CompositeProjectionFor("Trips", x =>
            {
                x.Add<TestingSupport.TripProjection>();
                x.Add<DayProjection>();
            });
        }, true);

        // Finding the document types correctly, preliminary step
        theStore.Options.Storage.AllDocumentMappings.Any(x => x.DocumentType == typeof(Trip)).ShouldBeTrue();
        theStore.Options.Storage.AllDocumentMappings.Any(x => x.DocumentType == typeof(Day)).ShouldBeTrue();

        // Another precondition that was a problem
        theStore.Options.Storage.MappingFor(typeof(Trip))
            .UseNumericRevisions.ShouldBeTrue();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await daemon.WaitForNonStaleData(5.Seconds());

        // Check Trip. This does verify that we get AggregateStreamAsync() working correctly,
        // so that's all good!
        await CheckAllExpectedAggregatesAgainstActuals();

        var days = await theSession.Query<Day>().ToListAsync();
        var expected = Streams.SelectMany(x => x.Events).OfType<IDayEvent>().Select(x => x.Day)
            .Distinct().ToArray();

        days.Count.ShouldBe(expected.Length);
        foreach (var day in expected)
        {
            days.Any(x => x.Id == day).ShouldBeTrue();
        }

        // Persist all of the progressions of the constituent parts
        var progressions = await theStore.Advanced.AllProjectionProgress();
        progressions.Single(x => x.ShardName == "Trips:All").Sequence.ShouldBe(NumberOfEvents);
        progressions.Single(x => x.ShardName == "Trip:All").Sequence.ShouldBe(NumberOfEvents);
        progressions.Single(x => x.ShardName == "Day:All").Sequence.ShouldBe(NumberOfEvents);

        var trips = await theSession.Query<Trip>().ToListAsync();

        var persisted = trips[0];
        var latest = await theSession.Events.FetchLatest<Trip>(persisted.Id);
        var stream = await theSession.Events.FetchForWriting<Trip>(persisted.Id);

        latest.ShouldBe(persisted);
        stream.Aggregate.ShouldBe(persisted);
    }
}
