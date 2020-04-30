using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_with_aggregate_functions : IntegrationContextWithIdentityMap<NulloIdentityMap>
    {
        // SAMPLE: using_max
        [Fact]
        public void get_max()
        {
            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 42 });
            theSession.Store(new Target { Color = Colors.Green, Number = 3 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

            theSession.SaveChanges();
            var maxNumber = theSession.Query<Target>().Max(t => t.Number);
            maxNumber.ShouldBe(42);
        }
        // ENDSAMPLE

        [Fact]
        public async Task get_max_async()
        {
            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 42 });
            theSession.Store(new Target { Color = Colors.Green, Number = 3 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

            theSession.SaveChanges();
            var maxNumber = await theSession.Query<Target>().MaxAsync(t => t.Number).ConfigureAwait(false);
            maxNumber.ShouldBe(42);
        }

        // SAMPLE: using_min
        [Fact]
        public void get_min()
        {
            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 2 });
            theSession.Store(new Target { Color = Colors.Green, Number = -5 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 42 });

            theSession.SaveChanges();
            var minNumber = theSession.Query<Target>().Min(t => t.Number);
            minNumber.ShouldBe(-5);
        }
        // ENDSAMPLE

        [Fact]
        public async Task get_min_async()
        {
            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 42 });
            theSession.Store(new Target { Color = Colors.Green, Number = -5 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

            theSession.SaveChanges();
            var maxNumber = await theSession.Query<Target>().MinAsync(t => t.Number).ConfigureAwait(false);
            maxNumber.ShouldBe(-5);
        }

        // SAMPLE: using_average
        [Fact]
        public void get_average()
        {
            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 2 });
            theSession.Store(new Target { Color = Colors.Green, Number = -5 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 42 });

            theSession.SaveChanges();
            var average = theSession.Query<Target>().Average(t => t.Number);
            average.ShouldBe(10);
        }
        // ENDSAMPLE

        [Fact]
        public async Task get_average_async()
        {
            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 42 });
            theSession.Store(new Target { Color = Colors.Green, Number = -5 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 2 });

            theSession.SaveChanges();
            var maxNumber = await theSession.Query<Target>().AverageAsync(t => t.Number).ConfigureAwait(false);
            maxNumber.ShouldBe(10);
        }

        [Fact]
        public void min_on_empty_table_should_throw()
        {
            Exception<InvalidOperationException>.ShouldBeThrownBy(()=> theSession.Query<Target>().Min(t => t.Number));
        }

        [Fact]
        public void max_on_empty_table_should_throw()
        {
            Exception<InvalidOperationException>.ShouldBeThrownBy(() => theSession.Query<Target>().Max(t => t.Number));
        }

        [Fact]
        public void average_on_empty_table_should_throw()
        {
            var e = Exception<InvalidOperationException>.ShouldBeThrownBy(() => theSession.Query<Target>().Average(t => t.Number));
            e.Message.ShouldBe("The cast to value type 'System.Double' failed because the materialized value is null. Either the result type's generic parameter or the query must use a nullable type.");
        }

        public query_with_aggregate_functions(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
