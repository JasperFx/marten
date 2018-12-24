using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Services;
using Marten.Testing.Events.Projections;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class fetching_stream_state_before_aggregator_is_registered : IntegratedFixture
    {
        [Fact]
        public async Task bug_705_order_of_operation()
        {
            var streamId = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.StartStream<QuestParty>(streamId, joined, departed);
                session.SaveChanges();
            }

            using (var query = theStore.OpenSession())
            {
                var state = await query.Events.FetchStreamStateAsync(streamId);
                var aggregate = await query.Events.AggregateStreamAsync<QuestParty>(streamId);
                
                

                state.ShouldNotBeNull();
                aggregate.ShouldNotBeNull();
            }


        }

        [Fact]
        public void other_try()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventTypes(new[] { typeof(FooEvent), });
            });

            using (var session = store.OpenSession())
            {
                var aid = Guid.Parse("1442cbbb-a49a-497e-9ee8-715ed2833bf8");
                session.Events.StartStream<FooAggregate>(aid, new FooEvent());
                session.SaveChanges();
            }

            var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventTypes(new[] { typeof(FooEvent), });

                _.Events.AggregateFor<FooAggregate>();
            });

            using (var session = store2.OpenSession())
            {
                var aid = Guid.Parse("1442cbbb-a49a-497e-9ee8-715ed2833bf8");
                var state = session.Events.FetchStreamState(aid);
                // We never get to the AggregateStream call because we get a nullreference exception on the FetchStreamState call
                var aggregate = session.Events.AggregateStream<FooAggregate>(aid);
            }
        }
    }

    public class FooEvent { }

    public class FooAggregate
    {
        public Guid Id;
    }


    // SAMPLE: fetching_stream_state
    public class fetching_stream_state : DocumentSessionFixture<NulloIdentityMap>
    {
        private Guid theStreamId;

        public fetching_stream_state()
        {
            var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            theStreamId = theSession.Events.StartStream<Quest>(joined, departed).Id;
            theSession.SaveChanges();
        }



        [Fact]
        public void can_fetch_the_stream_version_and_aggregate_type()
        {
            var state = theSession.Events.FetchStreamState(theStreamId);

            state.Id.ShouldBe(theStreamId);
            state.Version.ShouldBe(2);
            state.AggregateType.ShouldBe(typeof(Quest));
            state.LastTimestamp.ShouldNotBe(DateTime.MinValue);
            state.Created.ShouldNotBe(DateTime.MinValue);
        }

        [Fact]
        public async Task can_fetch_the_stream_version_and_aggregate_type_async()
        {
            var state = await theSession.Events.FetchStreamStateAsync(theStreamId).ConfigureAwait(false);

            state.Id.ShouldBe(theStreamId);
            state.Version.ShouldBe(2);
            state.AggregateType.ShouldBe(typeof(Quest));
            state.LastTimestamp.ShouldNotBe(DateTime.MinValue);
            state.Created.ShouldNotBe(DateTime.MinValue);
        }

        [Fact]
        public async Task can_fetch_the_stream_version_through_batch_query()
        {
            var batch = theSession.CreateBatchQuery();

            var stateTask = batch.Events.FetchStreamState(theStreamId);

            await batch.Execute().ConfigureAwait(false);

            var state = await stateTask.ConfigureAwait(false);

            state.Id.ShouldBe(theStreamId);
            state.Version.ShouldBe(2);
            state.AggregateType.ShouldBe(typeof(Quest));
            state.LastTimestamp.ShouldNotBe(DateTime.MinValue);

        }

        [Fact]
        public async Task can_fetch_the_stream_events_through_batch_query()
        {
            var batch = theSession.CreateBatchQuery();

            var eventsTask = batch.Events.FetchStream(theStreamId);

            await batch.Execute().ConfigureAwait(false);

            var events = await eventsTask.ConfigureAwait(false);

            events.Count.ShouldBe(2);
        }

        [Fact]
        public async Task will_call_apply_for_base_class()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventTypes(new[] { typeof(ChildEvent), typeof(BaseEvent), typeof(NonInheritied) });

                _.Events.AggregateFor<TestAggregateOnBase>();
            });
            using (var session = store.OpenSession()) {
                var id = Guid.NewGuid();
                session.Events.StartStream<TestAggregateOnBase>(id, new ChildEvent(), new NonInheritied(), new BaseEvent());
                await session.SaveChangesAsync();
                var proj = await session.Events.AggregateStreamAsync<TestAggregateOnBase>(id);
                proj.EventCount.ShouldBe(7);
            }
        }
        [Fact]
        public async Task will_call_apply_for_child_when_both_present()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventTypes(new[] { typeof(ChildEvent), typeof(BaseEvent), typeof(NonInheritied) });

                _.Events.AggregateFor<TestAggregateBoth>();
            });
            using (var session = store.OpenSession()) {
                var id = Guid.NewGuid();
                session.Events.StartStream<TestAggregateBoth>(id, new ChildEvent(), new NonInheritied(), new BaseEvent());
                await session.SaveChangesAsync();
                var proj = await session.Events.AggregateStreamAsync<TestAggregateBoth>(id);
                proj.EventCount.ShouldBe(106);
            }
        }
        [Fact]
        public async Task will_not_call_apply_for_base_if_no_apply()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventTypes(new[] { typeof(ChildEvent), typeof(BaseEvent), typeof(NonInheritied) });

                _.Events.AggregateFor<TestAggregateNoBase>();
            });
            using (var session = store.OpenSession()) {
                var id = Guid.NewGuid();
                session.Events.StartStream<TestAggregateNoBase>(id, new ChildEvent(), new NonInheritied(), new BaseEvent());
                await session.SaveChangesAsync();
                var proj = await session.Events.AggregateStreamAsync<TestAggregateNoBase>(id);
                proj.EventCount.ShouldBe(105);
            }
        }

        [Fact]
        public async Task will_call_apply_with_interface()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventTypes(new[] { typeof(ChildEvent), typeof(BaseEvent), typeof(NonInheritied), typeof(DumbInterfaceEvent) });

                _.Events.AggregateFor<TestAggregateInterface>();
            });
            using (var session = store.OpenSession()) {
                var id = Guid.NewGuid();
                session.Events.StartStream<TestAggregateInterface>(id, new ChildEvent(), new NonInheritied(), new BaseEvent(), new DumbInterfaceEvent());
                await session.SaveChangesAsync();
                var proj = await session.Events.AggregateStreamAsync<TestAggregateInterface>(id);
                proj.EventCount.ShouldBe(1000);
            }
        }

        [Fact]
        public async Task will_call_apply_with_object()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventTypes(new[] { typeof(ChildEvent), typeof(BaseEvent), typeof(NonInheritied), typeof(DumbInterfaceEvent) });

                _.Events.AggregateFor<TestAggregateObject>();
            });
            using (var session = store.OpenSession()) {
                var id = Guid.NewGuid();
                session.Events.StartStream<TestAggregateObject>(id, new ChildEvent(), new NonInheritied(), new BaseEvent());
                await session.SaveChangesAsync();
                var proj = await session.Events.AggregateStreamAsync<TestAggregateObject>(id);
                proj.EventCount.ShouldBe(3000000);
            }
        }

        [Fact]
        public async Task will_call_interface_over_parent()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventTypes(new[] { typeof(ChildEvent), typeof(BaseEvent), typeof(NonInheritied), typeof(DumbInterfaceEvent) });

                _.Events.AggregateFor<TestAggregateInterfaceOrBase>();
            });
            using (var session = store.OpenSession()) {
                var id = Guid.NewGuid();
                session.Events.StartStream<TestAggregateInterfaceOrBase>(id, new ChildEvent(), new NonInheritied(), new BaseEvent(), new DumbInterfaceEvent());
                await session.SaveChangesAsync();
                var proj = await session.Events.AggregateStreamAsync<TestAggregateInterfaceOrBase>(id);
                proj.EventCount.ShouldBe(1002);
            }
        }

        [Fact]
        public async Task will_call_apply_for_base_class_with_metadata()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventTypes(new[] { typeof(ChildEvent), typeof(BaseEvent), typeof(NonInheritied) });

                _.Events.AggregateFor<TestAggregateMetaData>();
            });
            using (var session = store.OpenSession()) {
                var id = Guid.NewGuid();
                session.Events.StartStream<TestAggregateMetaData>(id, new ChildEvent(), new NonInheritied(), new BaseEvent());
                await session.SaveChangesAsync();
                var proj = await session.Events.AggregateStreamAsync<TestAggregateMetaData>(id);
                proj.EventCount.ShouldBe(20);
            }
        }

        [Fact]
        public async Task will_call_traverse_to_object_and_do_nothing()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventTypes(new[] { typeof(ChildEvent), typeof(BaseEvent), typeof(NonInheritied) });

                _.Events.AggregateFor<TestAggregateInterface>();
            });
            using (var session = store.OpenSession()) {
                var id = Guid.NewGuid();
                session.Events.StartStream<TestAggregateInterface>(id, new ChildEvent(), new NonInheritied(), new BaseEvent());
                await session.SaveChangesAsync();
                var proj = await session.Events.AggregateStreamAsync<TestAggregateInterface>(id);
                proj.EventCount.ShouldBe(0);
            }
        }                

        public class BaseEvent {}
        public class ChildEvent : BaseEvent {}
        public class NonInheritied {}

        public class DumbInterfaceEvent : BaseEvent, DumbInterface {}

        public interface DumbInterface {}

        public class TestAggregateInterface {
            public Guid Id {get; set;}
            public int EventCount { get; set; } = 0;
            public void Apply(DumbInterface e) {
                EventCount += 1000;
            }
        }

        public class TestAggregateInterfaceOrBase {
            public Guid Id {get; set;}
            public int EventCount { get; set; } = 0;
            public void Apply(DumbInterface e) {
                EventCount += 1000;
            }
            public void Apply(BaseEvent e) {
                EventCount++;
            }
        }

        public class TestAggregateMetaData {
            public Guid Id { get; set; }
            public int EventCount { get; set; } = 0;

            public void Apply(Event<BaseEvent> e) {
                EventCount += 10;
            }
        }        

        public class TestAggregateObject {
            public Guid Id { get; set; }
            public int EventCount { get; set; } = 0;

            public void Apply(object e) {
                EventCount += 1000000;
            }
        }

        public class TestAggregateOnBase {
            public Guid Id { get; set; }
            public int EventCount { get; set; } = 0;

            public void Apply(BaseEvent e) {
                EventCount++;
            }
            public void Apply(NonInheritied e) {
                EventCount += 5;
            }            
        }

        public class TestAggregateNoBase {
            public Guid Id {get; set;}
            public int EventCount { get; set; } = 0;

            public void Apply(ChildEvent e) {
                EventCount += 100;
            }

            public void Apply(NonInheritied e) {
                EventCount += 5;
            }
        }

        public class TestAggregateBoth {
            public Guid Id {get; set;}
            public int EventCount  { get; set; } = 0;
            public void Apply(ChildEvent e) {
                EventCount += 100;
            }
            public void Apply(BaseEvent e) {
                EventCount += 1;
            }
            public void Apply(NonInheritied e) {
                EventCount += 5;
            }            
        }
    }
    // ENDSAMPLE
}