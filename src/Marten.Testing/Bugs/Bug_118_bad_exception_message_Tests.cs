using System.Linq;
using Marten.Linq;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_118_bad_exception_message_Tests: DocumentSessionFixture<NulloIdentityMap>
    {
        public class TestClass
        {
            public int Id { get; set; }
        }

        internal TestClass TestNullObject { get; set; }

        [Fact]
        public void When_Property_Is_Null_Exception_Should_Be_Null_Reference_Exception()
        {
            Exception<BadLinqExpressionException>.ShouldBeThrownBy(() =>
            {
                theSession.Query<TestClass>().Where(x => x.Id == TestNullObject.Id).ToList();
            });
        }
    }
}
