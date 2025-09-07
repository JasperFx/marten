using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class live_aggregation_without_an_aggregate_identifier : OneOffConfigurationsContext
{
    [Fact]
    public async Task live_aggregation_with_guid_identifiers()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId, new AEvent(), new BEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var version1 = await theSession.Events.AggregateStreamAsync<CountOfLetters>(streamId);

        var version2 = await theSession.Events.FetchLatest<CountOfLetters>(streamId);

        var version3 = await theSession.Events.FetchForWriting<CountOfLetters>(streamId);

        var version4 = await theSession.Events.AggregateStreamToLastKnownAsync<CountOfLetters>(streamId);

        version1.ACount.ShouldBe(1);
        version1.BCount.ShouldBe(2);
        version1.CCount.ShouldBe(1);
        version1.DCount.ShouldBe(0);

        version2.ACount.ShouldBe(1);
        version2.BCount.ShouldBe(2);
        version2.CCount.ShouldBe(1);
        version2.DCount.ShouldBe(0);

        version3.Aggregate.ACount.ShouldBe(1);
        version3.Aggregate.BCount.ShouldBe(2);
        version3.Aggregate.CCount.ShouldBe(1);
        version3.Aggregate.DCount.ShouldBe(0);

        version4.ACount.ShouldBe(1);
        version4.BCount.ShouldBe(2);
        version4.CCount.ShouldBe(1);
        version4.DCount.ShouldBe(0);
    }

    [Fact]
    public async Task live_aggregation_with_string_identifiers()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream(streamId, new AEvent(), new BEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var version1 = await theSession.Events.AggregateStreamAsync<CountOfLetters>(streamId);

        var version2 = await theSession.Events.FetchLatest<CountOfLetters>(streamId);

        var version3 = await theSession.Events.FetchForWriting<CountOfLetters>(streamId);

        var version4 = await theSession.Events.AggregateStreamToLastKnownAsync<CountOfLetters>(streamId);

        version1.ACount.ShouldBe(1);
        version1.BCount.ShouldBe(2);
        version1.CCount.ShouldBe(1);
        version1.DCount.ShouldBe(0);

        version2.ACount.ShouldBe(1);
        version2.BCount.ShouldBe(2);
        version2.CCount.ShouldBe(1);
        version2.DCount.ShouldBe(0);

        version3.Aggregate.ACount.ShouldBe(1);
        version3.Aggregate.BCount.ShouldBe(2);
        version3.Aggregate.CCount.ShouldBe(1);
        version3.Aggregate.DCount.ShouldBe(0);

        version4.ACount.ShouldBe(1);
        version4.BCount.ShouldBe(2);
        version4.CCount.ShouldBe(1);
        version4.DCount.ShouldBe(0);
    }
}


public class CountOfLetters
{
    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
    public int ECount { get; set; }

    public void Apply(AEvent _)
    {
        ACount++;
    }

    public void Apply(BEvent _)
    {
        BCount++;
    }

    public void Apply(CEvent _)
    {
        CCount++;
    }

    public void Apply(DEvent _)
    {
        DCount++;
    }

    public void Apply(EEvent _)
    {
        ECount++;
    }

}

