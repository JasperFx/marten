using System.Diagnostics;
using Marten.Generation;
using Marten.Testing.Generation;
using Npgsql;
using Shouldly;

namespace Marten.Testing
{
    public class PlayingTests
    {
        public void try_it_out()
        {

            Debug.WriteLine(ConnectionSource.ConnectionString);

            using (var connection = new NpgsqlConnection(ConnectionSource.ConnectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "select * from fake";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Debug.WriteLine(reader.GetString(0));
                    }
                }
            }
        }

        public void try_command_runner()
        {
            var builder = new SchemaBuilder();
            builder.CreateTable(typeof(SchemaBuilderTests.MySpecialDocument));

            using (var runner = new CommandRunner(ConnectionSource.ConnectionString))
            {
                runner.Execute(builder.ToSql());
            }
        }
    }
}