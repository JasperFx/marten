using System;
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
                    session.Store(new Target{Number = 1, Date = DateTime.Today});
                    session.Store(new Target{Number = 2, Date = DateTime.Today.AddDays(1)});
                    session.Store(new Target{Number = 3, Date = DateTime.Today.AddDays(2)});
                    session.Store(new Target{Number = 4, Date = DateTime.Today});
                    session.Store(new Target{Number = 5, Date = DateTime.Today.AddDays(1)});

                    session.SaveChanges();

                    var today = DateTime.Today;
                    session.Query<Target>().Where(x => x.Date == today).ToArray()
                        .Select(x => x.Number)
                        .ShouldHaveTheSameElementsAs(1, 4);
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