using System.Linq;
using System.Threading.Tasks;
using Marten.Services.Json;
using Marten.Testing.Harness;
using Marten.Testing.Linq.Compatibility.Support;
using Xunit;

namespace Marten.Testing.Linq.Compatibility
{
    public class select_transforms: LinqTestContext<DefaultQueryFixture, select_transforms>
    {
        public select_transforms(DefaultQueryFixture fixture) : base(fixture)
        {
        }

        static select_transforms()
        {
            selectInOrder(docs => docs.OrderBy(x => x.Id).Take(10).Select(x => new Person { Name = x.String, Number = x.Number }));
        }

        [SerializerTypeTargetedTheory(RunFor = SerializerType.Newtonsoft)]
        [MemberData(nameof(GetDescriptions))]
        public Task run_query(string description)
        {
            return assertTestCase(description, Fixture.Store);
        }
    }

    public class Person
    {
        public int Number { get; set; }
        public string Name { get; set; }
    }
}
