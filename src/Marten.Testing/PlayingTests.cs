using System;
using System.Diagnostics;
using Marten.Generation;
using Marten.Testing.Generation;
using Npgsql;
using NpgsqlTypes;
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
            builder.DefineUpsert(typeof (SchemaBuilderTests.MySpecialDocument));

            var id = Guid.NewGuid();

            using (var runner = new CommandRunner(ConnectionSource.ConnectionString))
            {
                runner.Execute(builder.ToSql());

                runner.Execute("mt_upsert_myspecialdocument", command =>
                {
                    command.Parameters.Add("docId", NpgsqlDbType.Uuid).Value = id;
                    command.Parameters.Add("doc", NpgsqlDbType.Json).Value = "{\"id\":\"1\"}";
                });

                runner.Execute("mt_upsert_myspecialdocument", command =>
                {
                    command.Parameters.Add("docId", NpgsqlDbType.Uuid).Value = id;
                    command.Parameters.Add("doc", NpgsqlDbType.Json).Value = "{\"id\":\"2\"}";
                });
            }
        }
    }
}