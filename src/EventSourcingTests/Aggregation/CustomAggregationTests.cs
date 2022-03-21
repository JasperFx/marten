using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Linq.SoftDeletes;
using Marten.Metadata;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation
{
    public class CustomAggregationTests
    {
        [Fact]
        public void default_projection_name_is_type_name()
        {
            new MyCustomAggregation().ProjectionName.ShouldBe(nameof(MyCustomAggregation));
        }

        [Fact]
        public void default_lifecycle_should_be_async()
        {
            new MyCustomAggregation().Lifecycle.ShouldBe(ProjectionLifecycle.Async);
        }

        [Fact]
        public void async_options_is_not_null()
        {
            new MyCustomAggregation().As<IProjectionSource>().Options.ShouldNotBeNull();
        }

        [Fact]
        public void assert_invalid_with_no_slicer()
        {
            Exception<InvalidProjectionException>.ShouldBeThrownBy(() =>
            {
                new MyCustomAggregateWithNoSlicer().CompileAndAssertValidity();
            });
        }

        [Fact]
        public void assert_invalid_with_incomplete_slicing_rules()
        {
            var projection = new MyCustomAggregateWithNoSlicer();
            projection.AggregateEvents(x => {});

            Exception<InvalidProjectionException>.ShouldBeThrownBy(() =>
            {
                new MyCustomAggregateWithNoSlicer().CompileAndAssertValidity();
            });
        }

        [Fact]
        public void valid_slicing_with_configured_slicing()
        {
            var projection = new MyCustomAggregateWithNoSlicer();
            projection.AggregateEvents(x => x.Identity<INumbered>(n => n.Number));

            projection.Slicer.ShouldBeOfType<EventSlicer<CustomAggregate, int>>();
        }

        [Fact]
        public void throws_if_you_try_to_slice_by_string_on_something_besides_guid_or_string()
        {
            var wrong = new EmptyCustomProjection<User, int>();

            Exception<InvalidProjectionException>.ShouldBeThrownBy(() =>
            {
                wrong.AggregateByStream();
            });
        }

        [Fact]
        public void use_per_stream_aggregation_by_guid()
        {
            var withGuid = new EmptyCustomProjection<User, Guid>();
            withGuid.AggregateByStream();

            withGuid.Slicer.ShouldBeOfType<ByStreamId<User>>();
        }

        [Fact]
        public void use_per_stream_aggregation_by_string()
        {
            var withGuid = new EmptyCustomProjection<StringDoc, string>();
            withGuid.AggregateByStream();

            withGuid.Slicer.ShouldBeOfType<ByStreamKey<StringDoc>>();
        }

    }

    public class EmptyCustomProjection<TDoc, TId>: CustomAggregation<TDoc, TId>
    {
        public override ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<TDoc, TId> slice, CancellationToken cancellation,
            ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
        {
            throw new NotImplementedException();
        }
    }

    public class custom_aggregation_end_to_end: OneOffConfigurationsContext
    {
        private void appendCustomEvent(int number, char letter)
        {
            theSession.Events.Append(Guid.NewGuid(), new CustomEvent(number, letter));
        }

        [Fact]
        public async Task use_inline_asynchronous()
        {
            StoreOptions(opts => opts.Projections.Add(new MyCustomAggregation(), ProjectionLifecycle.Inline));

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
                .ShouldBe(new CustomAggregate{Id = 1, ACount = 4, BCount = 1, CCount = 1, DCount = 1});

            (await theSession.LoadAsync<CustomAggregate>(2))
                .ShouldBe(new CustomAggregate{Id = 2, ACount = 2, BCount = 0, CCount = 0, DCount = 0});

            (await theSession.LoadAsync<CustomAggregate>(3))
                .ShouldBe(new CustomAggregate{Id = 3, ACount = 0, BCount = 1, CCount = 0, DCount = 1});

        }

        [Fact]
        public void use_inline_synchronous()
        {
            StoreOptions(opts => opts.Projections.Add(new MyCustomAggregation(), ProjectionLifecycle.Inline));

            theStore.Advanced.Clean.DeleteAllDocuments();
            theStore.Advanced.Clean.DeleteAllEventData();

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

            theSession.SaveChanges();

            theSession.Load<CustomAggregate>(1)
                .ShouldBe(new CustomAggregate{Id = 1, ACount = 4, BCount = 1, CCount = 1, DCount = 1});

            theSession.Load<CustomAggregate>(2)
                .ShouldBe(new CustomAggregate{Id = 2, ACount = 2, BCount = 0, CCount = 0, DCount = 0});

            theSession.Load<CustomAggregate>(3)
                .ShouldBe(new CustomAggregate{Id = 3, ACount = 0, BCount = 1, CCount = 0, DCount = 1});

        }

    }

    public class CustomEvent : INumbered
    {
        public CustomEvent(int number, char letter)
        {
            Number = number;
            Letter = letter;
        }

        public int Number { get; set; }
        public char Letter { get; set; }
    }

    public interface INumbered
    {
        public int Number { get; }
    }

    public class MyCustomAggregateWithNoSlicer: CustomAggregation<CustomAggregate, int>
    {
        public override ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<CustomAggregate, int> slice, CancellationToken cancellation,
            ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
        {
            throw new NotImplementedException();
        }


    }


    public class MyCustomAggregation: CustomAggregation<CustomAggregate, int>
    {
        public MyCustomAggregation()
        {
            AggregateEvents(s =>
            {
                s.Identity<INumbered>(x => x.Number);
            });
        }

        public override ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<CustomAggregate, int> slice, CancellationToken cancellation,
            ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
        {
            var aggregate = slice.Aggregate ?? new CustomAggregate { Id = slice.Id };

            foreach (var @event in slice.Events())
            {
                if (@event.Data is CustomEvent e)
                {
                    switch (e.Letter)
                    {
                        case 'a':
                            aggregate.ACount++;
                            break;

                        case 'b':
                            aggregate.BCount++;
                            break;

                        case 'c':
                            aggregate.CCount++;
                            break;

                        case 'd':
                            aggregate.DCount++;
                            break;
                    }
                }
            }

            session.Store(aggregate);
            return new ValueTask();
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
            return Id == other.Id && ACount == other.ACount && BCount == other.BCount && CCount == other.CCount && DCount == other.DCount;
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

            if (obj.GetType() != this.GetType())
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
            return $"{nameof(Id)}: {Id}, {nameof(ACount)}: {ACount}, {nameof(BCount)}: {BCount}, {nameof(CCount)}: {CCount}, {nameof(DCount)}: {DCount}";
        }
    }

    public class using_custom_aggregate_with_soft_deletes_and_update_only_events : OneOffConfigurationsContext, IAsyncLifetime
    {
        public using_custom_aggregate_with_soft_deletes_and_update_only_events()
        {
            StoreOptions(opts => opts.Projections.Add(new StartAndStopProjection()));
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
    }

    #region sample_StartAndStopAggregate

    public class StartAndStopAggregate : ISoftDeleted
    {
        // These are Marten controlled
        public bool Deleted { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }

        public int Count { get; set; }

        public Guid Id { get; set; }

        public void Increment()
        {
            Count++;
        }
    }

    #endregion

    #region sample_custom_aggregate_events

    public class Start{}
    public class End{}
    public class Restart{}
    public class Increment{}

    #endregion

    #region sample_custom_aggregate_with_start_and_stop

    public class StartAndStopProjection: CustomAggregation<StartAndStopAggregate, Guid>
    {
        public StartAndStopProjection()
        {
            // I'm telling Marten that events are assigned to the aggregate
            // document by the stream id
            AggregateByStream();

            // This is an optional, but potentially important optimization
            // for the async daemon so that it sets up an allow list
            // of the event types that will be run through this projection
            IncludeType<Start>();
            IncludeType<End>();
            IncludeType<Restart>();
            IncludeType<Increment>();
        }

        public override ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<StartAndStopAggregate, Guid> slice, CancellationToken cancellation,
            ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
        {

            var aggregate = slice.Aggregate;


            foreach (var data in slice.AllData())
            {
                switch (data)
                {
                    case Start:
                        aggregate = new StartAndStopAggregate
                        {
                            // Have to assign the identity ourselves
                            Id = slice.Id
                        };
                        break;
                    case Increment when aggregate is { Deleted: false }:
                        // Use explicit code to only apply this event
                        // if the aggregate already exists
                        aggregate.Increment();
                        break;
                    case End when aggregate is { Deleted: false }:
                        // This will be a "soft delete" because the aggregate type
                        // implements the IDeleted interface
                        session.Delete(aggregate);
                        aggregate.Deleted = true; // Got to help Marten out a little bit here
                        break;
                    case Restart when (aggregate == null || aggregate.Deleted):
                        // Got to "undo" the soft delete status
                        session
                            .UndoDeleteWhere<StartAndStopAggregate>(x => x.Id == slice.Id);
                        break;
                }
            }

            // Apply any updates!
            if (aggregate != null)
            {
                session.Store(aggregate);
            }


            // We didn't do anything that required an asynchronous call
            return new ValueTask();
        }
    }

    #endregion
}
