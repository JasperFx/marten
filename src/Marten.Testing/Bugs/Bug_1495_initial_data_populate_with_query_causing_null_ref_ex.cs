using System;
using System.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1495_initial_data_populate_with_query_causing_null_ref_ex
    {
        [Fact]
        public void initial_data_should_populate_db_with_query_in_populate_method()
        {
            var store = DocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "Bug1495";

                _.Connection(ConnectionSource.ConnectionString);

                _.InitialData.Add(new InitialDataWithQuery(InitialWithQueryDatasets.Aggregates));
            });

            using (var session = store.QuerySession())
            {
                foreach (var initialAggregate in InitialWithQueryDatasets.Aggregates)
                {
                    var aggregate = session.Query<Aggregate1495>().First(x => x.Name == initialAggregate.Name);
                    aggregate.Name.ShouldBe(initialAggregate.Name);
                }
            }

            store.Dispose();
        }
    }

    public class InitialDataWithQuery: IInitialData
    {
        private readonly Aggregate1495[] _initialData;

        public InitialDataWithQuery(params Aggregate1495[] initialData)
        {
            _initialData = initialData;
        }

        public void Populate(IDocumentStore store)
        {
            using (var session = store.OpenSession())
            {
                if (!session.Query<Aggregate1495>().Any())
                {
                    session.Store(_initialData);
                    session.SaveChanges();
                }
            }
        }
    }

    public static class InitialWithQueryDatasets
    {
        public static readonly Aggregate1495[] Aggregates =
        {
            new Aggregate1495 { Name = "Aggregate 1" },
            new Aggregate1495 { Name = "Aggregate 2" }
        };
    }

    public class Aggregate1495
    {
        public Aggregate1495()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public string Name { get; set; }
    }
}
