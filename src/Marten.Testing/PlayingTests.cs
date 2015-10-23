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

                    session.Query<User>().Where(x => x.FirstName == "Han").Single()
                        .LastName.ShouldBe("Solo");
                    


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

                    reader.Close();
                }

                connection.Close();
            }
        }

    }
}