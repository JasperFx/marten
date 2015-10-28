using System.Diagnostics;
using Marten.Testing.Fixtures;
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
                    session.Store(new Target {Number = 1, NumberArray = new[] {1, 2, 3}});
                    session.Store(new Target {Number = 2, NumberArray = new[] {4, 5, 6}});
                    session.Store(new Target {Number = 3, NumberArray = new[] {2, 3, 4}});
                    session.Store(new Target {Number = 4});

                    session.SaveChanges();

                    /*
                    session.Query<Target>().Where(x => x.NumberArray.Contains(3)).ToArray()
                        .Select(x => x.Number)
                        .ShouldHaveTheSameElementsAs(1, 3);
                     */
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