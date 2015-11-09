using System;
using NpgsqlTypes;
using Shouldly;

namespace Marten.Testing
{
    public class TypeMappingsTests
    {
        public void execute_to_db_type_as_date()
        {
            TypeMappings.ToDbType(typeof(DateTime)).ShouldBe(NpgsqlDbType.Timestamp);
        }

        public void execute_to_db_type_as_int()
        {
            TypeMappings.ToDbType(typeof(int)).ShouldBe(NpgsqlDbType.Integer);
            TypeMappings.ToDbType(typeof(int?)).ShouldBe(NpgsqlDbType.Integer);
        }
    }
}