using System.Data.Common;
using System.Linq;
using System.Reflection;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class SingleFieldSelectorTests
    {
        private readonly DocumentMapping theMapping;
        private readonly SingleFieldSelector<string> theSelector;

        public SingleFieldSelectorTests()
        {
            theMapping = DocumentMapping.For<User>();
            var prop = ReflectionHelper.GetProperty<User>(x => x.FirstName);

            theSelector = new SingleFieldSelector<string>(theMapping, new MemberInfo[] { prop });
        }

        [Fact]
        public void resolve_returns_the_first_field()
        {
            var name = "Eric";

            var reader = Substitute.For<DbDataReader>();
            reader.IsDBNull(0).Returns(false);
            reader.GetFieldValue<string>(0).Returns(name);

            theSelector.Resolve(reader, null, null).ShouldBe(name);
        }

        [Fact]
        public void uses_the_sql_locator_as_the_single_field()
        {
            theSelector.SelectFields().Single()
                .ShouldBe("d.data ->> 'FirstName'");
        }
    }
}