using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Shouldly;

namespace Marten.Testing.Util
{
    public class CommandExtensionsTests
    {
        public void add_first_parameter()
        {
            var command = new NpgsqlCommand();

            var param = command.AddParameter("a");

            param.Value.ShouldBe("a");
            param.ParameterName.ShouldBe("arg0");

            param.NpgsqlDbType.ShouldBe(NpgsqlDbType.Text);

            command.Parameters.ShouldContain(param);
        }

        public void add_second_parameter()
        {
            var command = new NpgsqlCommand();

            command.AddParameter("a");
            var param = command.AddParameter("b");

            param.ParameterName.ShouldBe("arg1");
        }

        
    }
}