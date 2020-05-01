using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_449_IsOneOf_query_with_enum_types: BugIntegrationContext
    {
        [Fact]
        public void can_query_with_is_one_of_on_an_enum_type_with_jil()
        {
            var blue = new Target { Color = Colors.Blue };
            var red = new Target { Color = Colors.Red };
            var green = new Target { Color = Colors.Green };

            using (var session = theStore.OpenSession())
            {
                session.Store(blue, red, green);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var list = query.Query<Target>().Where(x => x.Color.IsOneOf(Colors.Blue, Colors.Red)).ToList();

                list.Count.ShouldBe(2);
                list.Select(x => x.Id).ShouldContain(blue.Id);
                list.Select(x => x.Id).ShouldContain(red.Id);
            }
        }

        [Fact]
        public void can_query_with_is_one_of_on_an_enum_type_with_newtonsoft()
        {
            StoreOptions(_ => _.Serializer(new JsonNetSerializer()));

            var blue = new Target { Color = Colors.Blue };
            var red = new Target { Color = Colors.Red };
            var green = new Target { Color = Colors.Green };

            using (var session = theStore.OpenSession())
            {
                session.Store(blue, red, green);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var list = query.Query<Target>().Where(x => x.Color.IsOneOf(Colors.Blue, Colors.Red)).ToList();

                list.Count.ShouldBe(2);
                list.Select(x => x.Id).ShouldContain(blue.Id);
                list.Select(x => x.Id).ShouldContain(red.Id);
            }
        }

    }
}
