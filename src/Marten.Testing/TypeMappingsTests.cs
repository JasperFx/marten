using Marten.Util;
using NpgsqlTypes;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
	public class TypeMappingsTests
    {
        [Fact]
        public void execute_to_db_type_as_int()
        {
            TypeMappings.ToDbType(typeof(int)).ShouldBe(NpgsqlDbType.Integer);
            TypeMappings.ToDbType(typeof(int?)).ShouldBe(NpgsqlDbType.Integer);
        }
    }
}