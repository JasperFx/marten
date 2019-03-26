using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Linq.Compatibility.Support;
using Xunit;

namespace Marten.Testing.Linq.Compatibility
{
    public class take_and_skip : LinqTestContext<DefaultQueryFixture, take_and_skip>
    {
        public take_and_skip(DefaultQueryFixture fixture) : base(fixture)
        {
        }

        static take_and_skip()
        {
            ordered(docs => docs.OrderBy(x => x.Long).Skip(20).Take(10));
            ordered(docs => docs.OrderBy(x => x.Long).Skip(10).Take(20));
            ordered(docs => docs.OrderBy(x => x.Long).Take(20));
            ordered(docs => docs.OrderBy(x => x.Long).Skip(15));
        }
        
        [Theory]
        [MemberData(nameof(GetDescriptions))]
        public Task run_query(string description)
        {
            return assertTestCase(description, Fixture.Store);
        }
    }
}