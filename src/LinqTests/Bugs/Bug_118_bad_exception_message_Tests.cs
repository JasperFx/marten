using System.Linq;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Marten;

namespace LinqTests.Bugs;

public class Bug_118_bad_exception_message_Tests: BugIntegrationContext
{
    public class TestClass
    {
        public int Id { get; set; }
    }

    internal TestClass TestNullObject { get; set; }

    [Fact]
    public async Task When_Property_Is_Null_Exception_Should_Be_Null_Reference_Exception()
    {
        await Should.ThrowAsync<BadLinqExpressionException>(async () =>
        {
            await theSession.Query<TestClass>().Where(x => x.Id == TestNullObject.Id).ToListAsync();
        });
    }

}
