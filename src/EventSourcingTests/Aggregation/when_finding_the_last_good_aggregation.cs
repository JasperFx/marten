using System;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class when_finding_the_last_good_aggregation : IntegrationContext
{
    public when_finding_the_last_good_aggregation(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task finding_last_aggregate_using_guid()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleMaybeDeletedAggregate>(streamId, new AEvent(), new BEvent(), new CEvent(),
            new DEvent(), new DeleteYourself());
        await theSession.SaveChangesAsync();

        // Tight semantics here.
        (await theSession.Events.AggregateStreamAsync<SimpleMaybeDeletedAggregate>(streamId)).ShouldBeNull();
        var aggregateAt4 = await theSession.Events.AggregateStreamAsync<SimpleMaybeDeletedAggregate>(streamId, 4);
        aggregateAt4.ShouldNotBeNull();

        var lastGood = await theSession.Events.AggregateStreamToLastKnownAsync<SimpleMaybeDeletedAggregate>(streamId);
        lastGood.ShouldNotBeNull();

        lastGood.ShouldBe(aggregateAt4);

        // Now, mess things up and start over!
        theSession.Events.Append(streamId, new DEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        var newLastGood =
            await theSession.Events.AggregateStreamToLastKnownAsync<SimpleMaybeDeletedAggregate>(streamId);
        newLastGood.ShouldNotBeNull();
        newLastGood.DCount.ShouldBe(2);
        newLastGood.ACount.ShouldBe(0);
        newLastGood.BCount.ShouldBe(0);
        newLastGood.CCount.ShouldBe(0);
    }

    [Fact]
    public async Task finding_last_aggregate_using_string()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAsStringMaybeDeletedAggregate>(streamKey, new AEvent(), new BEvent(), new CEvent(),
            new DEvent(), new DeleteYourself());
        await theSession.SaveChangesAsync();

        // Tight semantics here.
        (await theSession.Events.AggregateStreamAsync<SimpleAsStringMaybeDeletedAggregate>(streamKey)).ShouldBeNull();
        var aggregateAt4 = await theSession.Events.AggregateStreamAsync<SimpleAsStringMaybeDeletedAggregate>(streamKey, 4);
        aggregateAt4.ShouldNotBeNull();

        var lastGood = await theSession.Events.AggregateStreamToLastKnownAsync<SimpleAsStringMaybeDeletedAggregate>(streamKey);
        lastGood.ShouldNotBeNull();

        lastGood.ShouldBe(aggregateAt4);

        // Now, mess things up and start over!
        theSession.Events.Append(streamKey, new DEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        var newLastGood =
            await theSession.Events.AggregateStreamToLastKnownAsync<SimpleAsStringMaybeDeletedAggregate>(streamKey);
        newLastGood.ShouldNotBeNull();
        newLastGood.DCount.ShouldBe(2);
        newLastGood.ACount.ShouldBe(0);
        newLastGood.BCount.ShouldBe(0);
        newLastGood.CCount.ShouldBe(0);
    }
}

public record DeleteYourself;

public class SimpleMaybeDeletedAggregate : IRevisioned
{
    // This will be the aggregate version
    public int Version { get; set; }

    public bool ShouldDelete(DeleteYourself _) => true;

    public Guid Id { get;
        set; }

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

    protected bool Equals(SimpleMaybeDeletedAggregate other)
    {
        return Version == other.Version && Id.Equals(other.Id) && ACount == other.ACount && BCount == other.BCount && CCount == other.CCount && DCount == other.DCount && ECount == other.ECount;
    }

    public override bool Equals(object obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((SimpleMaybeDeletedAggregate)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Version, Id, ACount, BCount, CCount, DCount, ECount);
    }

    public override string ToString()
    {
        return
            $"{nameof(Version)}: {Version}, {nameof(Id)}: {Id}, {nameof(ACount)}: {ACount}, {nameof(BCount)}: {BCount}, {nameof(CCount)}: {CCount}, {nameof(DCount)}: {DCount}, {nameof(ECount)}: {ECount}";
    }
}

public class SimpleAsStringMaybeDeletedAggregate : IRevisioned
{
    protected bool Equals(SimpleAsStringMaybeDeletedAggregate other)
    {
        return Version == other.Version && Id == other.Id && ACount == other.ACount && BCount == other.BCount && CCount == other.CCount && DCount == other.DCount && ECount == other.ECount;
    }

    public override bool Equals(object obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((SimpleAsStringMaybeDeletedAggregate)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Version, Id, ACount, BCount, CCount, DCount, ECount);
    }

    // This will be the aggregate version
    public int Version { get; set; }

    public bool ShouldDelete(DeleteYourself _) => true;

    public string Id { get;
        set; }

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

    public override string ToString()
    {
        return
            $"{nameof(Version)}: {Version}, {nameof(Id)}: {Id}, {nameof(ACount)}: {ACount}, {nameof(BCount)}: {BCount}, {nameof(CCount)}: {CCount}, {nameof(DCount)}: {DCount}, {nameof(ECount)}: {ECount}";
    }
}
