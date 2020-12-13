using System;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events.V4Concepts.Aggregations
{
    public class when_using_inline_lambdas_to_define_the_projection : AggregationContext
    {
        private readonly ITestOutputHelper _output;

        public when_using_inline_lambdas_to_define_the_projection(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
        }

        [Fact]
        public async Task live_aggregation_sync_apply_and_default_create()
        {
            UsingDefinition(p =>
            {
                p.ProjectEvent<AEvent>(doc => doc.ACount++);
                p.ProjectEvent<BEvent>((doc, e) => doc.BCount++);
                p.ProjectEvent<CEvent>((doc, e) => new MyAggregate
                {
                    ACount = doc.ACount,
                    BCount = doc.BCount,
                    CCount = doc.CCount + 1,
                    DCount = doc.DCount
                });

                p.ProjectEvent<DEvent>(doc => new MyAggregate
                {
                    ACount = doc.ACount, BCount = doc.BCount, CCount = doc.CCount, DCount = doc.DCount + 1
                });
            });

            _output.WriteLine(_projection.SourceCode());

            var aggregate = await LiveAggregation(x =>
            {
                x.B();
                x.C();
                x.B();
                x.C();
                x.C();
                x.A();
                x.D();
            });

            aggregate.ACount.ShouldBe(1);
            aggregate.BCount.ShouldBe(2);
            aggregate.CCount.ShouldBe(3);
            aggregate.DCount.ShouldBe(1);
        }

        [Fact]
        public async Task sync_apply_and_specific_create()
        {
            UsingDefinition(p =>
            {
                p.ProjectEvent<AEvent>(doc => doc.ACount++);
                p.ProjectEvent<BEvent>((doc, e) => doc.BCount++);
                p.ProjectEvent<CEvent>((doc, e) => new MyAggregate
                {
                    ACount = doc.ACount,
                    BCount = doc.BCount,
                    CCount = doc.CCount + 1,
                    DCount = doc.DCount
                });

                p.ProjectEvent<DEvent>(doc => new MyAggregate
                {
                    ACount = doc.ACount, BCount = doc.BCount, CCount = doc.CCount, DCount = doc.DCount + 1
                });

                p.CreateEvent<CreateEvent>(e => new MyAggregate
                {
                    ACount = e.A, BCount = e.B, CCount = e.C, DCount = e.D
                });
            });

            var aggregate = await LiveAggregation(x =>
            {
                x.Add(new CreateEvent(2, 3, 4, 5));

                x.B();
                x.C();
                x.B();
                x.C();
                x.C();
                x.A();
                x.D();
            });

            aggregate.ACount.ShouldBe(3);
            aggregate.BCount.ShouldBe(5);
            aggregate.CCount.ShouldBe(7);
            aggregate.DCount.ShouldBe(6);
        }


        [Fact]
        public async Task maybe_delete_negative()
        {
            var stream1 = Guid.NewGuid();

            UsingDefinition(p =>
            {
                p.ProjectEvent<AEvent>(doc => doc.ACount++);
                p.ProjectEvent<BEvent>((doc, e) => doc.BCount++);
                p.ProjectEvent<CEvent>((doc, e) => new MyAggregate
                {
                    ACount = doc.ACount,
                    BCount = doc.BCount,
                    CCount = doc.CCount + 1,
                    DCount = doc.DCount
                });

                p.ProjectEvent<DEvent>(doc => new MyAggregate
                {
                    ACount = doc.ACount, BCount = doc.BCount, CCount = doc.CCount, DCount = doc.DCount + 1
                });

                p.DeleteEvent<Finished>((aggregate, e) => (aggregate.ACount + aggregate.BCount + aggregate.CCount + aggregate.DCount) > 10);
            });

            _output.WriteLine(_projection.SourceCode());

            await InlineProject(x =>
            {
                x.Streams[stream1].IsNew = true;
                x.Streams[stream1].Add(new CreateEvent(1, 1, 1, 1));
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].Add<Finished>();

            });

            using var query = theStore.QuerySession();

            var aggregate1 = await query.LoadAsync<MyAggregate>(stream1);
            aggregate1.ShouldNotBeNull();

        }

        [Fact]
        public async Task delete_by_type()
        {
            var stream1 = Guid.NewGuid();

            UsingDefinition(p =>
            {
                p.ProjectEvent<AEvent>(doc => doc.ACount++);
                p.ProjectEvent<BEvent>((doc, e) => doc.BCount++);
                p.ProjectEvent<CEvent>((doc, e) => new MyAggregate
                {
                    ACount = doc.ACount,
                    BCount = doc.BCount,
                    CCount = doc.CCount + 1,
                    DCount = doc.DCount
                });

                p.ProjectEvent<DEvent>(doc => new MyAggregate
                {
                    ACount = doc.ACount, BCount = doc.BCount, CCount = doc.CCount, DCount = doc.DCount + 1
                });

                p.DeleteEvent<Finished>();
            });

            _output.WriteLine(_projection.SourceCode());

            await InlineProject(x =>
            {
                x.Streams[stream1].IsNew = true;
                x.Streams[stream1].Add(new CreateEvent(1, 1, 1, 1));
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();

                // Don't delete
                //x.Streams[stream1].Add<Finished>();

            });

            using var query = theStore.QuerySession();

            var aggregate1 = await query.LoadAsync<MyAggregate>(stream1);
            aggregate1.ShouldNotBeNull();

            await InlineProject(x =>
            {
                x.Streams[stream1].Add<Finished>();

            });

            using var query2 = theStore.QuerySession();
            var aggregate2 = await query2.LoadAsync<MyAggregate>(stream1);
            aggregate2.ShouldBeNull();


        }

        public class SystemState
        {
            public Guid Id { get; set; }
            public bool CausesDelete { get; set; }
        }

        public class DeleteBasedOnState
        {
            public Guid StateId { get; set; }
        }

        [Fact]
        public async Task delete_based_on_system_state()
        {
            var state1 = new SystemState
            {
                CausesDelete = false
            };
            var state2 = new SystemState
            {
                CausesDelete = true
            };

            theSession.Store(state1, state2);
            await theSession.SaveChangesAsync();

            UsingDefinition(p =>
            {
                p.ProjectEvent<AEvent>(doc => doc.ACount++);
                p.ProjectEvent<BEvent>((doc, e) => doc.BCount++);
                p.ProjectEvent<CEvent>((doc, e) => new MyAggregate
                {
                    ACount = doc.ACount,
                    BCount = doc.BCount,
                    CCount = doc.CCount + 1,
                    DCount = doc.DCount
                });

                p.ProjectEvent<DEvent>(doc => new MyAggregate
                {
                    ACount = doc.ACount, BCount = doc.BCount, CCount = doc.CCount, DCount = doc.DCount + 1
                });

                p.DeleteEventAsync<DeleteBasedOnState>(async (session, aggregate, @event) =>
                {
                    var state = await session.LoadAsync<SystemState>(@event.StateId);
                    return state.CausesDelete;
                });
            });

            var stream1 = Guid.NewGuid();
            var stream2 = Guid.NewGuid();

            await InlineProject(x =>
            {
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();

                x.Streams[stream2].B();
                x.Streams[stream2].B();
                x.Streams[stream2].B();
            });

            // Run another stream to see it deleted

            await InlineProject(x =>
            {
                // Should not cause a deletion
                x.Streams[stream1].Add(new DeleteBasedOnState {StateId = state1.Id});

                // Should cause a deletion
                x.Streams[stream2].Add(new DeleteBasedOnState {StateId = state2.Id});
            });

            using var query = theStore.QuerySession();

            (await query.LoadAsync<MyAggregate>(stream1)).ShouldNotBeNull();
            (await query.LoadAsync<MyAggregate>(stream2)).ShouldBeNull();
        }

        [Fact]
        public async Task maybe_delete_negative_2()
        {
            var stream1 = Guid.NewGuid();

            UsingDefinition(p =>
            {
                p.ProjectEvent<AEvent>(doc => doc.ACount++);
                p.ProjectEvent<BEvent>((doc, e) => doc.BCount++);
                p.ProjectEvent<CEvent>((doc, e) => new MyAggregate
                {
                    ACount = doc.ACount,
                    BCount = doc.BCount,
                    CCount = doc.CCount + 1,
                    DCount = doc.DCount
                });

                p.ProjectEvent<DEvent>(doc => new MyAggregate
                {
                    ACount = doc.ACount, BCount = doc.BCount, CCount = doc.CCount, DCount = doc.DCount + 1
                });

                p.DeleteEvent<Finished>(e => !e.Nevermind);
            });

            await InlineProject(x =>
            {
                x.Streams[stream1].IsNew = true;
                x.Streams[stream1].Add(new CreateEvent(1, 1, 1, 1));
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();

                // This shouldn't trigger the delete
                x.Streams[stream1].Add(new Finished
                {
                    Nevermind = true
                });

            });

            using var query = theStore.QuerySession();

            var aggregate1 = await query.LoadAsync<MyAggregate>(stream1);
            aggregate1.ShouldNotBeNull();

        }

        [Fact]
        public async Task maybe_delete_positive()
        {
            var stream1 = Guid.NewGuid();

            UsingDefinition(p =>
            {
                p.ProjectEvent<AEvent>(doc => doc.ACount++);
                p.ProjectEvent<BEvent>((doc, e) => doc.BCount++);
                p.ProjectEvent<CEvent>((doc, e) => new MyAggregate
                {
                    ACount = doc.ACount,
                    BCount = doc.BCount,
                    CCount = doc.CCount + 1,
                    DCount = doc.DCount
                });

                p.ProjectEvent<DEvent>(doc => new MyAggregate
                {
                    ACount = doc.ACount, BCount = doc.BCount, CCount = doc.CCount, DCount = doc.DCount + 1
                });

                p.DeleteEvent<Finished>((aggregate, e) => (aggregate.ACount + aggregate.BCount + aggregate.CCount + aggregate.DCount) > 10);
            });


            await InlineProject(x =>
            {
                x.Streams[stream1].IsNew = true;
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();

                // This will trip off the finished, MaybeDelete logic
                x.Streams[stream1].Add<Finished>();

            });

            using var query = theStore.QuerySession();

            var aggregate1 = await query.LoadAsync<MyAggregate>(stream1);
            aggregate1.ShouldBeNull();

        }

                [Fact]
        public async Task maybe_delete_positive_2()
        {
            var stream1 = Guid.NewGuid();

            UsingDefinition(p =>
            {
                p.ProjectEvent<AEvent>(doc => doc.ACount++);
                p.ProjectEvent<BEvent>((doc, e) => doc.BCount++);
                p.ProjectEvent<CEvent>((doc, e) => new MyAggregate
                {
                    ACount = doc.ACount,
                    BCount = doc.BCount,
                    CCount = doc.CCount + 1,
                    DCount = doc.DCount
                });

                p.ProjectEvent<DEvent>(doc => new MyAggregate
                {
                    ACount = doc.ACount, BCount = doc.BCount, CCount = doc.CCount, DCount = doc.DCount + 1
                });

                p.DeleteEvent<Finished>((e) => !e.Nevermind);
            });


            await InlineProject(x =>
            {
                x.Streams[stream1].IsNew = true;
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();

                // This will trip off the finished, MaybeDelete logic
                x.Streams[stream1].Add<Finished>();

            });

            using var query = theStore.QuerySession();

            var aggregate1 = await query.LoadAsync<MyAggregate>(stream1);
            aggregate1.ShouldBeNull();

        }


        [Fact]
        public async Task async_create_and_apply_with_session()
        {
            var user1 = new User {UserName = "Creator"};
            var user2 = new User {UserName = "Updater"};

            theSession.Store(user1, user2);
            await theSession.SaveChangesAsync();

            UsingDefinition(p =>
            {
                p.CreateEvent<UserStarted>(async (@event, session) =>
                {
                    var user = await session.LoadAsync<User>(@event.UserId);
                    return new MyAggregate {Created = user.UserName};
                });

                p.ProjectEventAsync<UserUpdated>(async (session, a, @event) =>
                {
                    var user = await session.LoadAsync<User>(@event.UserId);
                    a.UpdatedBy = user.UserName;
                });

                p.ProjectEvent<AEvent>((a, e) => a.ACount++);
            });


            var aggregate = await LiveAggregation(x =>
            {
                x.Add(new UserStarted {UserId = user1.Id});
                x.Add(new UserUpdated {UserId = user2.Id});
            });

            aggregate.Created.ShouldBe(user1.UserName);
            aggregate.UpdatedBy.ShouldBe(user2.UserName);
        }


    }





}
