using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
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
                    session.Store(new User {FirstName = null, LastName = "Blank"});
                    
                    session.Store(new User {FirstName = "Han", LastName = "Solo"});
                    session.Store(new User {FirstName = "Luke", LastName = "Skywalker"});
                    session.Store(new User { FirstName = "Max", LastName = "Miller", Address = new Address{City = "Austin"}});
                    session.SaveChanges();

                    session.Query<User>().Single(x => x.FirstName == null)
                        .LastName.ShouldBe("Blank");
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