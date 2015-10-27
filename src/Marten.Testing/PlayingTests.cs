using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Marten.Testing.Documents;
using Npgsql;
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
                    
                    session.Store(new User {FirstName = "Han", LastName = "Solo"});
                    session.Store(new User {FirstName = "Luke", LastName = "Skywalker"});
                    session.Store(new User { FirstName = "Max", LastName = "Miller" });
                    session.SaveChanges();

                    session.Query<User>().OrderBy(x => x.LastName).OrderByDescending(x => x.FirstName)
                        .ToArray().Each(x =>
                        {
                            Debug.WriteLine("{0} {1}", x.FirstName, x.LastName);
                        });
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