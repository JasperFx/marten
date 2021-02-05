using System;
using System.Threading.Tasks;
using Marten.Testing.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events.Projections
{
    public class using_non_concrete_types_in_projections : AggregationContext
    {
        private readonly ITestOutputHelper _output;

        public using_non_concrete_types_in_projections(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
            UsingDefinition(p =>
            {
                p.ProjectEvent<ITabulator>((a, e) =>
                {
                    e.Apply(a);
                    return a;
                });

                p.ProjectEvent<EEvent>((a, _) =>
                {
                    a.ECount++;
                    return a;
                });
            });
        }

        [Fact]
        public async Task live_aggregation()
        {
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

            _output.WriteLine(_projection.SourceCode());

            aggregate.ACount.ShouldBe(1);
            aggregate.BCount.ShouldBe(2);
            aggregate.CCount.ShouldBe(3);
            aggregate.DCount.ShouldBe(1);
        }

        [Fact]
        public async Task inline_projection()
        {
            var stream1 = Guid.NewGuid();
            await InlineProject(x =>
            {
                x.Streams[stream1].A();
                x.Streams[stream1].B();
                x.Streams[stream1].A();
                x.Streams[stream1].D();
                x.Streams[stream1].A();
                x.Streams[stream1].B();
                x.Streams[stream1].B();
                x.Streams[stream1].C();
                x.Streams[stream1].C();
                x.Streams[stream1].E();
                x.Streams[stream1].E();
                x.Streams[stream1].E();
                x.Streams[stream1].E();
                x.Streams[stream1].A();
            });

            using var query = theStore.QuerySession();

            var aggregate = await query.LoadAsync<MyAggregate>(stream1);
            aggregate.ShouldNotBeNull();

            aggregate.ACount.ShouldBe(4);
            aggregate.BCount.ShouldBe(3);
            aggregate.CCount.ShouldBe(2);
            aggregate.DCount.ShouldBe(1);
            aggregate.ECount.ShouldBe(4);
        }
    }
}
