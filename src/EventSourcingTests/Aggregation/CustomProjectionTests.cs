using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Metadata;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Util;
using NSubstitute;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class CustomProjectionTests
{
    [Theory]
    [InlineData(true, EventAppendMode.Quick, ProjectionLifecycle.Inline, true)]
    [InlineData(true, EventAppendMode.Quick, ProjectionLifecycle.Async, false)]
    [InlineData(true, EventAppendMode.Quick, ProjectionLifecycle.Live, false)]
    [InlineData(true, EventAppendMode.Rich, ProjectionLifecycle.Inline, false)]
    [InlineData(false, EventAppendMode.Rich, ProjectionLifecycle.Inline, false)]
    [InlineData(false, EventAppendMode.Quick, ProjectionLifecycle.Inline, false)]
    public void configure_mapping(bool isSingleGrouper, EventAppendMode appendMode, ProjectionLifecycle lifecycle, bool useVersionFromStream)
    {
        var projection = isSingleGrouper ? (IAggregateProjection)new MySingleStreamProjection() : new MyCustomProjection();
        var mapping = DocumentMapping.For<MyAggregate>();
        mapping.StoreOptions.Events.AppendMode = appendMode;
        //projection.Lifecycle = lifecycle;

        //projection.ConfigureAggregateMapping(mapping, mapping.StoreOptions);
        throw new NotImplementedException("See if this should be rebuilt");
        mapping.UseVersionFromMatchingStream.ShouldBe(useVersionFromStream);
    }

    [Fact]
    public void caches_aggregate_caches_correctly()
    {
        var projection = new MyCustomProjection();
        var db = Substitute.For<IMartenDatabase>();
        db.Identifier.Returns("main");
        var tenant1 = new Tenant("one", db);
        var tenant2 = new Tenant("two", db);
        var tenant3 = new Tenant("three", db);

        var cache1 = projection.CacheFor(tenant1.TenantId);
        var cache2 = projection.CacheFor(tenant2.TenantId);
        var cache3 = projection.CacheFor(tenant3.TenantId);

        projection.CacheFor(tenant1.TenantId).ShouldBeSameAs(cache1);
        projection.CacheFor(tenant2.TenantId).ShouldBeSameAs(cache2);
        projection.CacheFor(tenant3.TenantId).ShouldBeSameAs(cache3);

        cache1.ShouldNotBeSameAs(cache2);
        cache1.ShouldNotBeSameAs(cache3);
        cache2.ShouldNotBeSameAs(cache3);
    }

    [Fact]
    public void build_nullo_cache_with_no_limit()
    {
        var projection = new MyCustomProjection { Options = { CacheLimitPerTenant = 0 } };

        var db = Substitute.For<IMartenDatabase>();
        db.Identifier.Returns("main");
        var tenant1 = new Tenant("one", db);
        var tenant2 = new Tenant("two", db);
        var tenant3 = new Tenant("three", db);

        projection.CacheFor(tenant1.TenantId).ShouldBeOfType<NulloAggregateCache<int, CustomAggregate>>();
        projection.CacheFor(tenant2.TenantId).ShouldBeOfType<NulloAggregateCache<int, CustomAggregate>>();
        projection.CacheFor(tenant3.TenantId).ShouldBeOfType<NulloAggregateCache<int, CustomAggregate>>();

    }

    [Fact]
    public void build_real_cache_with_limit()
    {
        var projection = new MyCustomProjection
        {
            Options = { CacheLimitPerTenant = 1000 }
        };

        var db = Substitute.For<IMartenDatabase>();
        db.Identifier.Returns("main");
        var tenant1 = new Tenant("one", db);
        var tenant2 = new Tenant("two", db);
        var tenant3 = new Tenant("three", db);

        projection.CacheFor(tenant1.TenantId).ShouldBeOfType<RecentlyUsedCache<int, CustomAggregate>>().Limit.ShouldBe(projection.Options.CacheLimitPerTenant);
        projection.CacheFor(tenant2.TenantId).ShouldBeOfType<RecentlyUsedCache<int, CustomAggregate>>().Limit.ShouldBe(projection.Options.CacheLimitPerTenant);
        projection.CacheFor(tenant3.TenantId).ShouldBeOfType<RecentlyUsedCache<int, CustomAggregate>>().Limit.ShouldBe(projection.Options.CacheLimitPerTenant);

    }

    [Fact]
    public void default_projection_name_is_type_name()
    {
        new MyCustomProjection().ProjectionName.ShouldBe(nameof(MyCustomProjection));
    }

    [Fact]
    public void default_lifecycle_should_be_async()
    {
        new MyCustomProjection().Lifecycle.ShouldBe(ProjectionLifecycle.Async);
    }

    [Fact]
    public void async_options_is_not_null()
    {
        new MyCustomProjection().As<IProjectionSource<IDocumentOperations, IQuerySession>>().Options.ShouldNotBeNull();
    }

    [Fact]
    public void assert_invalid_with_no_slicer()
    {
        Should.Throw<InvalidProjectionException>(() =>
        {
            new MyCustomAggregateWithNoSlicer().AssembleAndAssertValidity();
        });
    }

}

public class EmptyCustomProjection<TDoc, TId>: CustomProjection<TDoc, TId>
{
    public override ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<TDoc, TId> slice,
        CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
    {
        throw new NotImplementedException();
    }
}

public class custom_projection_end_to_end: OneOffConfigurationsContext
{
    private void appendCustomEvent(int number, char letter)
    {
        theSession.Events.Append(Guid.NewGuid(), new CustomEvent(number, letter));
    }

    [Fact]
    public async Task use_inline_asynchronous()
    {
        StoreOptions(opts => opts.Projections.Add(new MyCustomProjection(), ProjectionLifecycle.Inline));

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        appendCustomEvent(1, 'a');
        appendCustomEvent(1, 'a');
        appendCustomEvent(1, 'b');
        appendCustomEvent(1, 'c');
        appendCustomEvent(1, 'd');
        appendCustomEvent(2, 'a');
        appendCustomEvent(2, 'a');
        appendCustomEvent(3, 'b');
        appendCustomEvent(3, 'd');
        appendCustomEvent(1, 'a');
        appendCustomEvent(1, 'a');

        await theSession.SaveChangesAsync();

        var agg1 = await theSession.LoadAsync<CustomAggregate>(1);
        agg1
            .ShouldBe(new CustomAggregate
            {
                Id = 1,
                ACount = 4,
                BCount = 1,
                CCount = 1,
                DCount = 1
            });

        (await theSession.LoadAsync<CustomAggregate>(2))
            .ShouldBe(new CustomAggregate
            {
                Id = 2,
                ACount = 2,
                BCount = 0,
                CCount = 0,
                DCount = 0
            });

        (await theSession.LoadAsync<CustomAggregate>(3))
            .ShouldBe(new CustomAggregate
            {
                Id = 3,
                ACount = 0,
                BCount = 1,
                CCount = 0,
                DCount = 1
            });
    }

    [Fact]
    public async Task use_inline_synchronous()
    {
        StoreOptions(opts => opts.Projections.Add(new MyCustomProjection(), ProjectionLifecycle.Inline));

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        appendCustomEvent(1, 'a');
        appendCustomEvent(1, 'a');
        appendCustomEvent(1, 'b');
        appendCustomEvent(1, 'c');
        appendCustomEvent(1, 'd');
        appendCustomEvent(2, 'a');
        appendCustomEvent(2, 'a');
        appendCustomEvent(3, 'b');
        appendCustomEvent(3, 'd');
        appendCustomEvent(1, 'a');
        appendCustomEvent(1, 'a');

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<CustomAggregate>(1))
            .ShouldBe(new CustomAggregate
            {
                Id = 1,
                ACount = 4,
                BCount = 1,
                CCount = 1,
                DCount = 1
            });

        (await theSession.LoadAsync<CustomAggregate>(2))
            .ShouldBe(new CustomAggregate
            {
                Id = 2,
                ACount = 2,
                BCount = 0,
                CCount = 0,
                DCount = 0
            });

        (await theSession.LoadAsync<CustomAggregate>(3))
            .ShouldBe(new CustomAggregate
            {
                Id = 3,
                ACount = 0,
                BCount = 1,
                CCount = 0,
                DCount = 1
            });
    }

    [Fact]
    public async Task use_strong_typed_guid_based_identifier()
    {
        var mapping = new DocumentMapping(typeof(MyCustomGuidAggregate), new StoreOptions());
        mapping.IdMember.Name.ShouldBe("Id");

        StoreOptions(opts =>
        {
            opts.Projections.Add(new MyCustomGuidProjection(), ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<MyCustomGuidAggregate>(streamId, new AEvent(), new BEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.LoadAsync<MyCustomGuidAggregate>(new MyCustomGuidId(streamId));
        aggregate.A.ShouldBe(1);
        aggregate.B.ShouldBe(2);
        aggregate.C.ShouldBe(0);
    }

    [Fact]
    public async Task use_strong_typed_string_based_identifier()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add(new MyCustomStreamProjection(), ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<MyCustomStringAggregate>(streamId, new AEvent(), new BEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.LoadAsync<MyCustomStringAggregate>(new MyCustomStringId(streamId));
        aggregate.A.ShouldBe(1);
        aggregate.B.ShouldBe(2);
        aggregate.C.ShouldBe(0);
    }
}

public class CustomEvent: INumbered
{
    public CustomEvent(int number, char letter)
    {
        Number = number;
        Letter = letter;
    }

    public char Letter { get; set; }

    public int Number { get; set; }
}

public interface INumbered
{
    public int Number { get; }
}

public class MyCustomAggregateWithNoSlicer: CustomProjection<CustomAggregate, int>
{
    public override ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<CustomAggregate, int> slice,
        CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
    {
        throw new NotImplementedException();
    }
}

public class MySingleStreamProjection: SingleStreamProjection<CustomAggregate, Guid>
{
    public override CustomAggregate Evolve(CustomAggregate snapshot, Guid id, IEvent e)
    {
        return base.Evolve(snapshot, id, e);
    }
}

public record struct MyCustomStringId(string Value);

public class MyCustomStringAggregate
{
    public MyCustomStringId Id { get; set; }
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
    public int D { get; set; }
}

public class MyCustomStreamProjection: SingleStreamProjection<MyCustomStringAggregate, MyCustomStringId>
{
    public override MyCustomStringAggregate Evolve(MyCustomStringAggregate snapshot, MyCustomStringId id, IEvent e)
    {
        snapshot ??= new();

        switch (e.Data)
        {
            case AEvent:
                snapshot.A++;
                break;
            case BEvent:
                snapshot.B++;
                break;
            case CEvent:
                snapshot.C++;
                break;
            case DEvent:
                snapshot.D++;
                break;
        }

        return snapshot;
    }
}

public class MyCustomGuidAggregate
{
    public MyCustomGuidId Id { get; set; }
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
    public int D { get; set; }
}

public class MyCustomGuidProjection: SingleStreamProjection<MyCustomGuidAggregate, MyCustomGuidId>
{
    public override MyCustomGuidAggregate Evolve(MyCustomGuidAggregate snapshot, MyCustomGuidId id, IEvent e)
    {
        snapshot ??= new MyCustomGuidAggregate();

        switch (e.Data)
        {
            case AEvent:
                snapshot.A++;
                break;
            case BEvent:
                snapshot.B++;
                break;
            case CEvent:
                snapshot.C++;
                break;
            case DEvent:
                snapshot.D++;
                break;
        }

        return snapshot;
    }
}

public record struct MyCustomGuidId(Guid Value);

public class MyCustomProjection: MultiStreamProjection<CustomAggregate, int>
{
    public MyCustomProjection()
    {
        Identity<INumbered>(x => x.Number);
    }

    public override CustomAggregate Evolve(CustomAggregate snapshot, int id, IEvent e)
    {
        snapshot ??= new CustomAggregate { Id = id };
        if (e.Data is CustomEvent ce)
        {
            switch (ce.Letter)
            {
                case 'a':
                    snapshot.ACount++;
                    break;

                case 'b':
                    snapshot.BCount++;
                    break;

                case 'c':
                    snapshot.CCount++;
                    break;

                case 'd':
                    snapshot.DCount++;
                    break;
            }
        }

        return snapshot;

    }
}

public class CustomAggregate
{
    public int Id { get; set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }

    protected bool Equals(CustomAggregate other)
    {
        return Id == other.Id && ACount == other.ACount && BCount == other.BCount && CCount == other.CCount &&
               DCount == other.DCount;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
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

        return Equals((CustomAggregate)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, ACount, BCount, CCount, DCount);
    }

    public override string ToString()
    {
        return
            $"{nameof(Id)}: {Id}, {nameof(ACount)}: {ACount}, {nameof(BCount)}: {BCount}, {nameof(CCount)}: {CCount}, {nameof(DCount)}: {DCount}";
    }
}

public class using_custom_aggregate_with_soft_deletes_and_update_only_events: OneOffConfigurationsContext,
    IAsyncLifetime
{
    public using_custom_aggregate_with_soft_deletes_and_update_only_events()
    {
        StoreOptions(opts => opts.Projections.Add(new StartAndStopProjection(), ProjectionLifecycle.Inline));
    }

    public Task InitializeAsync()
    {
        return theStore.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task update_only_when_aggregate_does_not_exist()
    {
        var stream = Guid.NewGuid();

        // This should do nothing because the aggregate isn't started yet
        theSession.Events.StartStream(stream, new Increment(), new Increment());
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<StartAndStopAggregate>(stream)).ShouldBeNull();
    }

    [Fact]
    public async Task start_and_increment()
    {
        var stream = Guid.NewGuid();

        // This should do nothing because the aggregate isn't started yet
        theSession.Events.StartStream(stream, new Start(), new Increment(), new Increment());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.LoadAsync<StartAndStopAggregate>(stream);
        aggregate.Count.ShouldBe(2);
    }

    [Fact]
    public async Task trigger_initial_delete()
    {
        var stream = Guid.NewGuid();

        // This should do nothing because the aggregate isn't started yet
        theSession.Events.StartStream(stream, new Start(), new Increment(), new Increment());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.LoadAsync<StartAndStopAggregate>(stream);
        aggregate.ShouldNotBeNull();

        theSession.Events.Append(stream, new Increment(), new End(), new Increment());
        await theSession.SaveChangesAsync();

        aggregate = await theSession.LoadAsync<StartAndStopAggregate>(stream);
        aggregate.Count.ShouldBe(3);
        aggregate.Deleted.ShouldBeTrue();
    }

    [Fact]
    public async Task use_aggregate_stream_with_custom_projection()
    {
        var stream = Guid.NewGuid();

        // This should do nothing because the aggregate isn't started yet
        theSession.Events.StartStream(stream, new Start(), new Increment(), new Increment());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<StartAndStopAggregate>(stream);
        aggregate.ShouldNotBeNull();

        theSession.Events.Append(stream, new Increment(), new End(), new Increment());
        await theSession.SaveChangesAsync();

        aggregate = await theSession.Events.AggregateStreamAsync<StartAndStopAggregate>(stream);
        aggregate.Count.ShouldBe(3);
        aggregate.Deleted.ShouldBeTrue();
    }

    [Fact]
    public async Task use_fetch_latest_with_custom_projection()
    {
        var stream = Guid.NewGuid();

        // This should do nothing because the aggregate isn't started yet
        theSession.Events.StartStream(stream, new Start(), new Increment(), new Increment());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.FetchLatest<StartAndStopAggregate>(stream);
        aggregate.ShouldNotBeNull();

        theSession.Events.Append(stream, new Increment(), new End(), new Increment());
        await theSession.SaveChangesAsync();

        aggregate = await theSession.Events.FetchLatest<StartAndStopAggregate>(stream);
        aggregate.Count.ShouldBe(3);
        aggregate.Deleted.ShouldBeTrue();
    }

}

#region sample_StartAndStopAggregate

public class StartAndStopAggregate: ISoftDeleted
{
    public int Count { get; set; }

    public Guid Id { get; set; }

    // These are Marten controlled
    public bool Deleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public void Increment()
    {
        Count++;
    }
}

#endregion

#region sample_custom_aggregate_events

public class Start
{
}

public class End
{
}

public class Restart
{
}

public class Increment
{
}

#endregion

#region sample_custom_aggregate_with_start_and_stop

public class StartAndStopProjection: SingleStreamProjection<StartAndStopAggregate, Guid>
{
    public StartAndStopProjection()
    {
        // This is an optional, but potentially important optimization
        // for the async daemon so that it sets up an allow list
        // of the event types that will be run through this projection
        IncludeType<Start>();
        IncludeType<End>();
        IncludeType<Restart>();
        IncludeType<Increment>();
    }

    public override ValueTask<SnapshotAction<StartAndStopAggregate>> ApplyAsync(IQuerySession session, StartAndStopAggregate snapshot, Guid identity, IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        throw new NotImplementedException("Redo all of this");
        // var aggregate = snapshot ?? new StartAndStopAggregate();
        //
        // foreach (var data in events)
        // {
        //     switch (data)
        //     {
        //         case Start:
        //             aggregate = new StartAndStopAggregate
        //             {
        //                 // Have to assign the identity ourselves
        //                 Id = identity
        //             };
        //             break;
        //         case Increment when aggregate is { Deleted: false }:
        //             // Use explicit code to only apply this event
        //             // if the aggregate already exists
        //             aggregate.Increment();
        //             break;
        //         case End when aggregate is { Deleted: false }:
        //             // This will be a "soft delete" because the aggregate type
        //             // implements the IDeleted interface
        //             session.Delete(aggregate);
        //             aggregate.Deleted = true; // Got to help Marten out a little bit here
        //             break;
        //         case Restart when aggregate == null || aggregate.Deleted:
        //             // Got to "undo" the soft delete status
        //             session
        //                 .UndoDeleteWhere<StartAndStopAggregate>(x => x.Id == slice.Id);
        //             break;
        //     }
        // }
        //
        // // THIS IS IMPORTANT. *IF* you want to use a CustomProjection with
        // // AggregateStreamAsync(), you must make this call
        // // FetchLatest() will work w/o any other changes though
        // slice.Aggregate = aggregate;
        //
        // // Apply any updates!
        // if (aggregate != null)
        // {
        //     session.Store(aggregate);
        // }
        //
        // // We didn't do anything that required an asynchronous call
        // return new ValueTask();
    }


}

#endregion
