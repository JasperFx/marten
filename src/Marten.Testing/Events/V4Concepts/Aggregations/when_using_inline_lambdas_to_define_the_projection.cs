using System.Threading.Tasks;
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

    }
}
