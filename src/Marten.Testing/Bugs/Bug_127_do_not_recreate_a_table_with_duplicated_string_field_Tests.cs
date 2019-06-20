using System;
using System.Linq;
using Marten.Schema;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_127_do_not_recreate_a_table_with_duplicated_string_field_Tests
    {
        [Fact]
        public void does_not_recreate_the_table()
        {
            var store1 = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
            });

            store1.Advanced.Clean.CompletelyRemoveAll();

            using (var session1 = store1.OpenSession())
            {
                session1.Store(new Team { Name = "Warriors" });
                session1.Store(new Team { Name = "Spurs" });
                session1.Store(new Team { Name = "Thunder" });

                session1.SaveChanges();

                session1.Query<Team>().Count().ShouldBe(3);
            }

            var store2 = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
            });

            using (var session2 = store2.QuerySession())
            {
                session2.Query<Team>().Count().ShouldBe(3);
            }
        }
    }

    public class Team
    {
        public Guid Id { get; set; }

        [DuplicateField]
        public string Name { get; set; }
    }
}
