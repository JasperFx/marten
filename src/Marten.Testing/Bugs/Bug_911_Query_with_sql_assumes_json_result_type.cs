using System.Data.Common;
using Marten.Linq;
using Marten.Services;
using NSubstitute;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_911_Query_with_sql_assumes_json_result_type
    {
        [Fact]
        public void resolve_checks_for_simple_type()
        {
            var reader = Substitute.For<DbDataReader>();
            var serializer = new JsonNetSerializer();
            var selector = new DeserializeSelector<int>(serializer);
            reader.GetFieldValue<int>(0).Returns(10);

            var result = selector.Resolve(reader, null, null);

            Assert.Equal(10, result);
        }
    }
}
