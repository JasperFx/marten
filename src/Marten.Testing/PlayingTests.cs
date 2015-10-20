using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Marten.Generation;
using Marten.Testing.Documents;
using Marten.Testing.Generation;
using Npgsql;
using Shouldly;
using StructureMap;

namespace Marten.Testing
{
    public class PlayingTests
    {
        public void linq_spike()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container.GetInstance<IDocumentSession>())
                {
                    session.Store(new User {FirstName = "Jeremy", LastName = "Miller"});
                    session.Store(new User {FirstName = "Max", LastName = "Miller"});
                    session.Store(new User {FirstName = "Han", LastName = "Solo"});
                    session.SaveChanges();

                    var firstnames = session.Query<User>().Where(x => x.FirstName == "Han" || x.FirstName == "Max").ToArray()
                        .OrderBy(x => x.FirstName).Select(x => x.FirstName).ToArray();

                    firstnames.ShouldBe(new []{"Han", "Max"});
                    


                }
            }


        }

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
                /*
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
                 * */
                //runner.DescribeSchema();
                runner.SchemaFunctionNames().Each(x => Debug.WriteLine(x));
            }
        }
    }
}