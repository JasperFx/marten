using System;
using System.Threading.Tasks;
using DaemonTests.MultiTenancy;
using JasperFx.Core;
using Marten.Events.Projections;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Aggregations;

public class converting_projection_from_inline_to_async : OneOffConfigurationsContext
{
    [Fact]
    public async Task start_as_inline_move_to_async_and_just_continue()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);
        });

        var id1 = theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new BEvent()).Id;
        var id2 = theSession.Events.StartStream<SimpleAggregate>(new BEvent(), new CEvent()).Id;
        var id3 = theSession.Events.StartStream<SimpleAggregate>(new CEvent(), new DEvent()).Id;
        await theSession.SaveChangesAsync();

        var store2 = SeparateStore(opts =>
        {
            #region sample_using_subscribe_as_inline_to_async

            opts
                .Projections
                .Snapshot<SimpleAggregate>(SnapshotLifecycle.Async, o =>
                {
                    // This option tells Marten to start the async projection at the highest
                    // event sequence assigned as the processing floor if there is no previous
                    // async daemon progress for this projection
                    o.SubscribeAsInlineToAsync();
                });

            #endregion
        });

        using var daemon = await store2.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        using var session = store2.LightweightSession();
        session.Events.Append(id1, new EEvent(), new EEvent());
        session.Events.Append(id2, new EEvent(), new EEvent());
        session.Events.Append(id3, new EEvent(), new EEvent());
        await session.SaveChangesAsync();

        await daemon.WaitForNonStaleData(10.Seconds());

        var aggregate1 = await session.LoadAsync<SimpleAggregate>(id1);
        var aggregate2 = await session.LoadAsync<SimpleAggregate>(id2);
        var aggregate3 = await session.LoadAsync<SimpleAggregate>(id3);

        aggregate1.ShouldBe(new SimpleAggregate
        {
            Id = id1,
            Version = 4,
            ACount = 1,
            BCount = 1,
            ECount = 2
        });

        aggregate2.ShouldBe(new SimpleAggregate
        {
            Id = id2,
            Version = 4,
            BCount = 1,
            CCount = 1,
            ECount = 2
        });

    }
}

public class SimpleAggregate : IRevisioned
{
    // This will be the aggregate version
    public int Version { get; set; }

    public Guid Id { get; set; }

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

    protected bool Equals(SimpleAggregate other)
    {
        return Id.Equals(other.Id) && ACount == other.ACount && BCount == other.BCount && CCount == other.CCount && DCount == other.DCount && ECount == other.ECount;
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

        return Equals((SimpleAggregate)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, ACount, BCount, CCount, DCount, ECount);
    }
}
