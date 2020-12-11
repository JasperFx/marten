using System;
using System.Threading.Tasks;
using Marten.Events.V4Concept.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events.V4Concepts.Aggregations
{
    public class when_doing_inline_per_stream_aggregations_with_Guid_stream_identity : AggregationContext
    {
        private readonly ITestOutputHelper _output;

        public when_doing_inline_per_stream_aggregations_with_Guid_stream_identity(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
        }

        [Fact]
        public async Task explicitly_new_stream()
        {
            var stream1 = Guid.NewGuid();
            var stream2 = Guid.NewGuid();

            UsingDefinition<AllSync>();

            _output.WriteLine(_projection.SourceCode());

            await InlineProject(x =>
            {
                x.Streams[stream1].IsNew = true;
                x.Streams[stream1].Add(new CreateEvent(1, 2, 3, 4));
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();

                x.Streams[stream2].IsNew = true;
                x.Streams[stream2].Add(new CreateEvent(3, 3, 3, 3));
                x.Streams[stream2].A();
                x.Streams[stream2].B();
                x.Streams[stream2].C();
            });

            using var query = theStore.QuerySession();

            var aggregate1 = await query.LoadAsync<MyAggregate>(stream1);
            aggregate1.ACount.ShouldBe(4);
            aggregate1.BCount.ShouldBe(2);
            aggregate1.CCount.ShouldBe(3);
            aggregate1.DCount.ShouldBe(4);

            var aggregate2 = await query.LoadAsync<MyAggregate>(stream2);
            aggregate2.ACount.ShouldBe(4);
            aggregate2.BCount.ShouldBe(4);
            aggregate2.CCount.ShouldBe(4);
            aggregate2.DCount.ShouldBe(3);
        }

        [Fact]
        public async Task new_stream_but_not_marked_that_way()
        {
            var stream1 = Guid.NewGuid();
            var stream2 = Guid.NewGuid();

            UsingDefinition<AllSync>();

            _output.WriteLine(_projection.SourceCode());

            await InlineProject(x =>
            {
                x.Streams[stream1].Add(new CreateEvent(1, 2, 3, 4));
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();

                x.Streams[stream2].Add(new CreateEvent(3, 3, 3, 3));
                x.Streams[stream2].A();
                x.Streams[stream2].B();
                x.Streams[stream2].C();
            });

            using var query = theStore.QuerySession();

            var aggregate1 = await query.LoadAsync<MyAggregate>(stream1);
            aggregate1.ACount.ShouldBe(4);
            aggregate1.BCount.ShouldBe(2);
            aggregate1.CCount.ShouldBe(3);
            aggregate1.DCount.ShouldBe(4);

            var aggregate2 = await query.LoadAsync<MyAggregate>(stream2);
            aggregate2.ACount.ShouldBe(4);
            aggregate2.BCount.ShouldBe(4);
            aggregate2.CCount.ShouldBe(4);
            aggregate2.DCount.ShouldBe(3);
        }

        [Fact]
        public async Task adding_to_an_existing_stream()
        {
            var stream1 = Guid.NewGuid();
            var stream2 = Guid.NewGuid();

            theSession.Store(new MyAggregate
            {
                Id = stream1,
                ACount = 1,
                BCount = 2,
                CCount = 3,
                DCount = 4
            });

            theSession.Store(new MyAggregate
            {
                Id = stream2,
                ACount = 3,
                BCount = 3,
                CCount = 3,
                DCount = 3
            });

            await theSession.SaveChangesAsync();

            UsingDefinition<AllSync>();

            _output.WriteLine(_projection.SourceCode());

            await InlineProject(x =>
            {
                x.Streams[stream1].A();
                x.Streams[stream1].A();
                x.Streams[stream1].A();

                x.Streams[stream2].A();
                x.Streams[stream2].B();
                x.Streams[stream2].C();
            });

            using var query = theStore.QuerySession();

            var aggregate1 = await query.LoadAsync<MyAggregate>(stream1);
            aggregate1.ACount.ShouldBe(4);
            aggregate1.BCount.ShouldBe(2);
            aggregate1.CCount.ShouldBe(3);
            aggregate1.DCount.ShouldBe(4);

            var aggregate2 = await query.LoadAsync<MyAggregate>(stream2);
            aggregate2.ACount.ShouldBe(4);
            aggregate2.BCount.ShouldBe(4);
            aggregate2.CCount.ShouldBe(4);
            aggregate2.DCount.ShouldBe(3);
        }

        [Fact]
        public async Task maybe_delete_negative()
        {
            var stream1 = Guid.NewGuid();

            UsingDefinition<SometimesDeletes>();

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
        public async Task maybe_delete_positive()
        {
            var stream1 = Guid.NewGuid();

            UsingDefinition<SometimesDeletes>();


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

        public class Finished{}

        public class SometimesDeletes: V4AggregateProjection<MyAggregate>
        {
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

            public bool ShouldDelete(MyAggregate aggregate, Finished @event)
            {
                return (aggregate.ACount + aggregate.BCount + aggregate.CCount + aggregate.DCount) > 10;
            }
        }

    }
}
