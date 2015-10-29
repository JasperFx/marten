using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Npgsql;
using Octokit;
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
                    session.Store(new Target{Number = 1, NumberArray = new []{1, 2, 3}});
                    session.Store(new Target{Number = 2, NumberArray = new []{4, 5, 6}});
                    session.Store(new Target{Number = 3, NumberArray = new []{2, 3, 4}});
                    session.Store(new Target{Number = 4});

                    session.SaveChanges();

                    //session.Query<Target>("select data from mt_doc_target, jsonb_array_elements(data -> 'NumberArray') numbers where numbers @> ARRAY[3]").Each(x => Debug.WriteLine(x.Number));

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
            var runner = new CommandRunner(new ConnectionSource());

            runner.Execute(conn =>
            {
                var table = conn.GetSchema("Tables");

                foreach (DataRow row in table.Rows)
                {
                    Debug.WriteLine("{0} / {1} / {2}", row[0], row[1], row[2]);
                }
            });
        }

    }
}