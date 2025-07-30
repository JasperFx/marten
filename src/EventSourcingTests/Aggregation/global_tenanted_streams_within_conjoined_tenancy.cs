using System;
using System.Threading.Tasks;
using EventSourcingTests.FetchForWriting;
using FastExpressionCompiler.ImTools;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class global_tenanted_streams_within_conjoined_tenancy : OneOffConfigurationsContext
{
    public global_tenanted_streams_within_conjoined_tenancy()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.AddGlobalProjection(new SpecialCounterProjection(), ProjectionLifecycle.Inline);

            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);
        });

        theStore.Events.GlobalAggregates.ShouldContain(typeof(SpecialCounter));
    }

    [Fact]
    public void the_aggregate_document_should_be_single_tenanted()
    {
        // Any other document type
        theStore.StorageFeatures.MappingFor(typeof(SimpleAggregate)).TenancyStyle.ShouldBe(TenancyStyle.Conjoined);

        // THIS should be single tenanted
        theStore.StorageFeatures.MappingFor(typeof(SpecialCounter)).TenancyStyle.ShouldBe(TenancyStyle.Single);
    }

    [Fact]
    public void event_appender_should_be_a_decorator()
    {
        var decorator = theStore.Events.EventAppender.ShouldBeOfType<GlobalEventAppenderDecorator>();
        decorator.AggregateTypes.ShouldContain(typeof(SpecialCounter));

        decorator.EventTypes.ShouldContain(typeof(SpecialA));
        decorator.EventTypes.ShouldContain(typeof(SpecialB));
        decorator.EventTypes.ShouldContain(typeof(SpecialC));
        decorator.EventTypes.ShouldContain(typeof(SpecialD));
    }

    /*
     * Test cases
     * Rich vs Quick on all
     * default tenant id, just works
     * session created w/ tenant id, mixed tenanted and not tenanted streams w/ StartStream
     * session created w/ tenant id, mixed tenanted and not tenanted streams w/ Append
     * session created w/ tenant id, mixed tenanted and not tenanted streams w/ FetchForWriting
     * session created w/ tenant id, test FetchLatest
     * session created w/ tenant id, test FetchForWriting gives right results
     *
     */

    public Guid SpecialId = Guid.NewGuid();
    public Guid TenantedId = Guid.NewGuid();

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.Rich)]
    public async Task start_stream_to_default_tenant_id_session(EventAppendMode mode)
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.AddGlobalProjection(new SpecialCounterProjection(), ProjectionLifecycle.Inline);

            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);

            opts.Events.AppendMode = mode;
        });

        theSession.Events.StartStream<SpecialCounter>(SpecialId, new SpecialA(), new SpecialB());
        await theSession.SaveChangesAsync();

        var latest = await theSession.Events.FetchLatest<SpecialCounter>(SpecialId);
        latest.ACount.ShouldBe(1);
        latest.BCount.ShouldBe(1);
        latest.CCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.Rich)]
    public async Task start_stream_to_explicit_tenant_id_session(EventAppendMode mode)
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.AddGlobalProjection(new SpecialCounterProjection(), ProjectionLifecycle.Inline);

            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);

            opts.Events.AppendMode = mode;
        });

        using var session = theStore.LightweightSession("one");

        session.Events.StartStream<SimpleAggregate>(TenantedId, new AEvent(), new CEvent());
        session.Events.StartStream<SpecialCounter>(SpecialId, new SpecialA(), new SpecialB());
        await session.SaveChangesAsync();

        var latest = await session.Events.FetchLatest<SpecialCounter>(SpecialId);
        latest.ACount.ShouldBe(1);
        latest.BCount.ShouldBe(1);
        latest.CCount.ShouldBe(0);

        var tenanted = await session.Events.FetchLatest<SimpleAggregate>(TenantedId);
        tenanted.ShouldNotBeNull();

        // non-tenanted session, should be null
        (await theSession.Events.FetchLatest<SimpleAggregate>(TenantedId)).ShouldBeNull();

        // global should still be found
        (await theSession.Events.FetchLatest<SpecialCounter>(SpecialId)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Inline)]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Live)]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Async)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Inline)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Live)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Async)]
    public async Task append_to_explicit_tenant_id_session(EventAppendMode mode, ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.AddGlobalProjection(new SpecialCounterProjection(), lifecycle);

            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);

            opts.Events.AppendMode = mode;
        });

        using var session = theStore.LightweightSession("one");

        session.Events.StartStream<SimpleAggregate>(TenantedId, new AEvent(), new CEvent());
        session.Events.StartStream<SpecialCounter>(SpecialId, new SpecialA(), new SpecialB());
        await session.SaveChangesAsync();

        using var session2 = theStore.LightweightSession("two");
        session2.Events.Append(SpecialId,new SpecialC(), new SpecialC(), new SpecialC());
        await session2.SaveChangesAsync();

        var latest = await session2.Events.FetchLatest<SpecialCounter>(SpecialId);
        latest.ACount.ShouldBe(1);
        latest.BCount.ShouldBe(1);
        latest.CCount.ShouldBe(3);

        var tenanted = await session.Events.FetchLatest<SimpleAggregate>(TenantedId);
        tenanted.ShouldNotBeNull();

        // non-tenanted session, should be null
        (await theSession.Events.FetchLatest<SimpleAggregate>(TenantedId)).ShouldBeNull();

        // global should still be found
        (await theSession.Events.FetchLatest<SpecialCounter>(SpecialId)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Inline)]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Live)]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Async)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Inline)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Live)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Async)]
    public async Task fetch_for_writing_to_explicit_tenant_id_session(EventAppendMode mode, ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.AddGlobalProjection(new SpecialCounterProjection(), lifecycle);

            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);

            opts.Events.AppendMode = mode;
        });

        using var session = theStore.LightweightSession("one");

        session.Events.StartStream<SimpleAggregate>(TenantedId, new AEvent(), new CEvent());
        session.Events.StartStream<SpecialCounter>(SpecialId, new SpecialA(), new SpecialB());
        await session.SaveChangesAsync();

        using var session2 = theStore.LightweightSession("two");
        var stream = await session2.Events.FetchForWriting<SpecialCounter>(SpecialId);
        stream.AppendMany(new SpecialC(), new SpecialC(), new SpecialC());
        await session2.SaveChangesAsync();

        var latest = await theSession.Events.FetchLatest<SpecialCounter>(SpecialId);
        latest.ACount.ShouldBe(1);
        latest.BCount.ShouldBe(1);
        latest.CCount.ShouldBe(3);

        var tenanted = await session.Events.FetchLatest<SimpleAggregate>(TenantedId);
        tenanted.ShouldNotBeNull();

        // non-tenanted session, should be null
        (await theSession.Events.FetchLatest<SimpleAggregate>(TenantedId)).ShouldBeNull();

        // global should still be found
        (await theSession.Events.FetchLatest<SpecialCounter>(SpecialId)).ShouldNotBeNull();
    }

    /******************* AS STRING **********************/

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.Rich)]
    public async Task start_stream_to_default_tenant_id_session_as_string(EventAppendMode mode)
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.AddGlobalProjection(new SpecialCounterProjectionAsString(), ProjectionLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;

            opts.Events.AppendMode = mode;
        });

        theSession.Events.StartStream<SpecialCounterAsString>(SpecialId.ToString(), new SpecialA(), new SpecialB());
        await theSession.SaveChangesAsync();

        var latest = await theSession.Events.FetchLatest<SpecialCounterAsString>(SpecialId.ToString());
        latest.ACount.ShouldBe(1);
        latest.BCount.ShouldBe(1);
        latest.CCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.Rich)]
    public async Task start_stream_to_explicit_tenant_id_session_as_string(EventAppendMode mode)
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.AddGlobalProjection(new SpecialCounterProjectionAsString(), ProjectionLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;

            opts.Events.AppendMode = mode;
        });

        using var session = theStore.LightweightSession("one");

        session.Events.StartStream<SpecialCounterAsString>(SpecialId.ToString(), new SpecialA(), new SpecialB());
        await session.SaveChangesAsync();

        var latest = await session.Events.FetchLatest<SpecialCounterAsString>(SpecialId.ToString());
        latest.ACount.ShouldBe(1);
        latest.BCount.ShouldBe(1);
        latest.CCount.ShouldBe(0);

        // global should still be found
        (await theSession.Events.FetchLatest<SpecialCounterAsString>(SpecialId.ToString())).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Inline)]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Live)]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Async)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Inline)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Live)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Async)]
    public async Task append_to_explicit_tenant_id_session_as_string(EventAppendMode mode, ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.AddGlobalProjection(new SpecialCounterProjectionAsString(), lifecycle);
            opts.Events.StreamIdentity = StreamIdentity.AsString;

            opts.Events.AppendMode = mode;
        });

        using var session = theStore.LightweightSession("one");

        session.Events.StartStream<SpecialCounterAsString>(SpecialId.ToString(), new SpecialA(), new SpecialB());
        await session.SaveChangesAsync();

        using var session2 = theStore.LightweightSession("two");
        session2.Events.Append(SpecialId.ToString(),new SpecialC(), new SpecialC(), new SpecialC());
        await session2.SaveChangesAsync();

        var latest = await session2.Events.FetchLatest<SpecialCounterAsString>(SpecialId.ToString());
        latest.ACount.ShouldBe(1);
        latest.BCount.ShouldBe(1);
        latest.CCount.ShouldBe(3);

        // global should still be found
        (await theSession.Events.FetchLatest<SpecialCounterAsString>(SpecialId.ToString())).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Inline)]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Live)]
    [InlineData(EventAppendMode.Quick, ProjectionLifecycle.Async)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Inline)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Live)]
    [InlineData(EventAppendMode.Rich, ProjectionLifecycle.Async)]
    public async Task fetch_for_writing_to_explicit_tenant_id_session_as_string(EventAppendMode mode, ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.AddGlobalProjection(new SpecialCounterProjectionAsString(), lifecycle);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AppendMode = mode;
        });

        using var session = theStore.LightweightSession("one");

        session.Events.StartStream<SpecialCounter>(SpecialId.ToString(), new SpecialA(), new SpecialB());
        await session.SaveChangesAsync();

        using var session2 = theStore.LightweightSession("two");
        var stream = await session2.Events.FetchForWriting<SpecialCounterAsString>(SpecialId.ToString());
        stream.AppendMany(new SpecialC(), new SpecialC(), new SpecialC());
        await session2.SaveChangesAsync();

        var latest = await theSession.Events.FetchLatest<SpecialCounterAsString>(SpecialId.ToString());
        latest.ACount.ShouldBe(1);
        latest.BCount.ShouldBe(1);
        latest.CCount.ShouldBe(3);

        // global should still be found
        (await theSession.Events.FetchLatest<SpecialCounterAsString>(SpecialId.ToString())).ShouldNotBeNull();
    }



    public static void boostrapping_sample()
    {
        #region sample_bootstrapping_with_global_projection

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            // The event store has conjoined tenancy...
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;

            // But we want any events appended to a stream that is related
            // to a SpecialCounter to be single or global tenanted
            // And this works with any ProjectionLifecycle
            opts.Projections.AddGlobalProjection(new SpecialCounterProjection(), ProjectionLifecycle.Inline);
        });

        #endregion
    }

}

public record SpecialA;
public record SpecialB;
public record SpecialC;
public record SpecialD;

public class SpecialCounter
{
    public Guid Id { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
}

#region sample_SpecialCounterProjection

public class SpecialCounterProjection: SingleStreamProjection<SpecialCounter, Guid>
{
    public void Apply(SpecialCounter c, SpecialA _) => c.ACount++;
    public void Apply(SpecialCounter c, SpecialB _) => c.BCount++;
    public void Apply(SpecialCounter c, SpecialC _) => c.CCount++;
    public void Apply(SpecialCounter c, SpecialD _) => c.DCount++;

}

#endregion



#region sample_SpecialCounterProjection2

public class SpecialCounterProjection2: SingleStreamProjection<SpecialCounter, Guid>
{
    public SpecialCounterProjection2()
    {
        // This is normally just an optimization for the async daemon,
        // but as a "global" projection, this also helps Marten
        // "know" that all events of these types should always be captured
        // to the default tenant id
        IncludeType<SpecialA>();
        IncludeType<SpecialB>();
        IncludeType<SpecialC>();
        IncludeType<SpecialD>();
    }

    public void Apply(SpecialCounter c, SpecialA _) => c.ACount++;
    public void Apply(SpecialCounter c, SpecialB _) => c.BCount++;
    public void Apply(SpecialCounter c, SpecialC _) => c.CCount++;
    public void Apply(SpecialCounter c, SpecialD _) => c.DCount++;

    public override SpecialCounter Evolve(SpecialCounter snapshot, Guid id, IEvent e)
    {
        snapshot ??= new SpecialCounter { Id = id };
        switch (e.Data)
        {
            case SpecialA _:
                snapshot.ACount++;
                break;
            case SpecialB _:
                snapshot.BCount++;
                break;
            case SpecialC _:
                snapshot.CCount++;
                break;
            case SpecialD _:
                snapshot.DCount++;
                break;
        }

        return snapshot;
    }
}

#endregion


public class SpecialCounterAsString
{
    public string Id { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
}

public class SpecialCounterProjectionAsString: SingleStreamProjection<SpecialCounterAsString, string>
{
    public SpecialCounterProjectionAsString()
    {
        // This has to be globally or single tenanted within an
        // otherwise multi-tenanted system
        // GlobalTenancy();
    }

    public void Apply(SpecialCounterAsString c, SpecialA _) => c.ACount++;
    public void Apply(SpecialCounterAsString c, SpecialB _) => c.BCount++;
    public void Apply(SpecialCounterAsString c, SpecialC _) => c.CCount++;
    public void Apply(SpecialCounterAsString c, SpecialD _) => c.DCount++;

}
