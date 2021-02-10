using System;
using System.Threading.Tasks;
using Marten.Testing.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events.Bugs
{
    public class Bug_1723_inline_projections_get_cut_off : AggregationContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_1723_inline_projections_get_cut_off(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
        }

        [Fact]
        public async Task big_streams()
        {
            var stream1 = Guid.NewGuid();
            var stream2 = Guid.NewGuid();

            UsingDefinition<AllSync>();

            _output.WriteLine(_projection.SourceCode());

            await InlineProject(x =>
            {
                x.Streams[stream1].IsNew = true;
                x.Streams[stream1].Add(new CreateEvent(1, 2, 3, 4));

                for (int i = 0; i < 20; i++)
                {
                    x.Streams[stream1].A();
                    x.Streams[stream1].B();
                    x.Streams[stream1].B();
                    x.Streams[stream1].C();
                    x.Streams[stream1].C();
                    x.Streams[stream1].C();
                }

                x.Streams[stream2].IsNew = true;
                x.Streams[stream2].Add(new CreateEvent(3, 3, 3, 3));

                for (int i = 0; i < 100; i++)
                {
                    x.Streams[stream2].A();
                    x.Streams[stream2].B();
                    x.Streams[stream2].C();
                }


            });

            using var query = theStore.QuerySession();

            var aggregate1 = await query.LoadAsync<MyAggregate>(stream1);
            aggregate1.ACount.ShouldBe(21);
            aggregate1.BCount.ShouldBe(42);
            aggregate1.CCount.ShouldBe(63);

            var aggregate2 = await query.LoadAsync<MyAggregate>(stream2);
            aggregate2.ACount.ShouldBe(103);
            aggregate2.BCount.ShouldBe(103);

        }
    }
}
